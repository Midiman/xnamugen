﻿using System;
using System.Diagnostics;
using xnaMugen.Collections;
using System.Collections.Generic;
using xnaMugen.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;

namespace xnaMugen.StateMachine
{
	class StateManager
	{
		public StateManager(StateSystem statesystem, Combat.Character character, ReadOnlyKeyedCollection<Int32, State> states)
		{
			if (statesystem == null) throw new ArgumentNullException("statesystem");
			if (character == null) throw new ArgumentNullException("character");
			if (states == null) throw new ArgumentNullException("states");

			m_statesystem = statesystem;
			m_character = character;
			m_states = states;
			m_persistencemap = new Dictionary<StateController, Int32>();
			m_foreignmanager = null;
			m_statetime = 0;

#if DEBUG
			m_stateorder = new CircularBuffer<State>(10);
#else
			m_currentstate = null;
			m_previousstate = null;
#endif
		}

		public void Clear()
		{
			m_foreignmanager = null;
			m_persistencemap.Clear();
			m_statetime = 0;

#if DEBUG
			m_stateorder.Clear();
#else
			m_currentstate = null;
			m_previousstate = null;
#endif
		}

		public StateManager Clone(Combat.Character character)
		{
			if (character == null) throw new ArgumentNullException("character");

			return new StateManager(StateSystem, character, States);
		}

		void ApplyState(State state)
		{
			if (state == null) throw new ArgumentNullException("state");

			m_persistencemap.Clear();

			if (state.Physics != Physics.Unchanged) Character.Physics = state.Physics;
			if (state.StateType != StateType.Unchanged) Character.StateType = state.StateType;
			if (state.MoveType != MoveType.Unchanged) Character.MoveType = state.MoveType;

			Int32? playercontrol = EvaluationHelper.AsInt32(m_character, state.PlayerControl, null);
			if (playercontrol != null) Character.PlayerControl = (playercontrol > 0) ? PlayerControl.InControl : PlayerControl.NoControl;

			Int32? animationnumber = EvaluationHelper.AsInt32(m_character, state.AnimationNumber, null);
			if (animationnumber != null) Character.SetLocalAnimation(animationnumber.Value, 0);

			Int32? spritepriority = EvaluationHelper.AsInt32(m_character, state.SpritePriority, null);
			if (spritepriority != null) Character.DrawOrder = spritepriority.Value;

			Int32? power = EvaluationHelper.AsInt32(m_character, state.Power, null);
			if (power != null) Character.BasePlayer.Power += power.Value;

			Vector2? velocity = EvaluationHelper.AsVector2(m_character, state.Velocity, null);
			if (velocity != null) Character.CurrentVelocity = velocity.Value;

			Boolean hitdefpersistance = EvaluationHelper.AsBoolean(m_character, state.HitdefPersistance, false);
			if (hitdefpersistance == false)
			{
				Character.OffensiveInfo.ActiveHitDef = false;
				Character.OffensiveInfo.HitPauseTime = 0;
			}

			Boolean movehitpersistance = EvaluationHelper.AsBoolean(m_character, state.MovehitPersistance, false);
			if (movehitpersistance == false)
			{
				Character.OffensiveInfo.MoveReversed = 0;
				Character.OffensiveInfo.MoveHit = 0;
				Character.OffensiveInfo.MoveGuarded = 0;
				Character.OffensiveInfo.MoveContact = 0;
			}

			Boolean hitcountpersistance = EvaluationHelper.AsBoolean(m_character, state.HitCountPersistance, false);
			if (hitcountpersistance == false)
			{
				Character.OffensiveInfo.HitCount = 0;
				Character.OffensiveInfo.UniqueHitCount = 0;
			}
		}

		public Boolean ChangeState(Int32 statenumber)
		{
			if (statenumber < 0) throw new ArgumentOutOfRangeException("statenumber", "Cannot change to state with number less than zero");

			State state = GetState(statenumber, false);

			if (state == null)
			{
				return false;
			}

#if DEBUG
			m_stateorder.Add(state);
#else
			m_previousstate = m_currentstate;
			m_currentstate = state;
#endif
			m_statetime = -1;
			return true;
		}

		void RunCurrentStateLoop(Boolean hitpause)
		{
			while (true)
			{
				if (m_statetime == -1)
				{
					m_statetime = 0;
					ApplyState(CurrentState);
				}

				if (RunState(CurrentState, hitpause) == false) break;
			}
		}

		public void Run(Boolean hitpause)
		{
			if (Character is Combat.Helper)
			{
				if ((Character as Combat.Helper).Data.KeyControl == true)
				{
					RunState(-1, true, hitpause);
				}
			}
			else
			{
				if (ForeignManager == null) RunState(-3, true, hitpause);
				RunState(-2, true, hitpause);
				RunState(-1, true, hitpause);
			}

			RunCurrentStateLoop(hitpause);

			if (hitpause == false)
			{
				++m_statetime;
			}
		}

		Boolean RunState(Int32 statenumber, Boolean forcelocal, Boolean hitpause)
		{
			State state = GetState(statenumber, forcelocal);
			if (state != null) return RunState(state, hitpause);

			return false;
		}

		State GetState(Int32 statenumber, Boolean forcelocal)
		{
			if (ForeignManager != null && forcelocal == false) return ForeignManager.GetState(statenumber, true);

			return (States.Contains(statenumber) == true) ? States[statenumber] : null;
		}

		Boolean RunState(State state, Boolean hitpause)
		{
			if (state == null) throw new ArgumentNullException("state");

			foreach (StateController controller in state.Controllers)
			{
				if (hitpause == true && controller.IgnoreHitPause == false) continue;

                Boolean persistencecheck = state.Number < 0 || PersistenceCheck(controller, controller.Persistence);
				if (persistencecheck == false) continue;

				Boolean triggercheck = controller.Triggers.Trigger(m_character);
				if (triggercheck == false) continue;

                if (controller.Persistence == 0 || controller.Persistence > 1) m_persistencemap[controller] = controller.Persistence;

				controller.Run(m_character);

				if (controller is Controllers.ChangeState || controller is Controllers.SelfState) return true;
			}

			return false;
		}

		Boolean PersistenceCheck(StateController controller, Int32 persistence)
		{
			if (controller == null) throw new ArgumentNullException("controller");

			if (m_persistencemap.ContainsKey(controller) == false) return true;

			if (persistence == 0)
			{
				return false;
			}
			else if (persistence == 1)
			{
				return true;
			}
			else if (persistence > 1)
			{
				m_persistencemap[controller] += -1;

				return m_persistencemap[controller] <= 0;
			}
			else
			{
				return false;
			}
		}

		public StateSystem StateSystem
		{
			get { return m_statesystem; }
		}

		public Combat.Character Character
		{
			get { return m_character; }
		}

		ReadOnlyKeyedCollection<Int32, State> States
		{
			get { return m_states; }
		}

		public Int32 StateTime
		{
			get { return m_statetime; }
		}

		public State CurrentState
		{
			get 
			{
#if DEBUG
				return (StateOrder.Size > 0) ? StateOrder.ReverseGet(0) : null; 
#else
				return m_currentstate;
#endif
			}
		}

		public State PreviousState
		{
			get
			{
#if DEBUG
				return (StateOrder.Size > 1) ? StateOrder.ReverseGet(1) : null; 
#else
				return m_previousstate;
#endif
			}
		}

#if DEBUG
		CircularBuffer<State> StateOrder
		{
			get { return m_stateorder; }
		}
#endif

		public StateManager ForeignManager
		{
			get { return m_foreignmanager; }

			set { m_foreignmanager = value; }
		}

		#region Fields

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		readonly StateSystem m_statesystem;

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		readonly Combat.Character m_character;

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		readonly ReadOnlyKeyedCollection<Int32, State> m_states;

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		readonly Dictionary<StateController, Int32> m_persistencemap;

#if DEBUG
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		readonly CircularBuffer<State> m_stateorder;
#else
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		State m_currentstate;

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		State m_previousstate;
#endif

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		StateManager m_foreignmanager;

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		Int32 m_statetime;

		#endregion
	}
}