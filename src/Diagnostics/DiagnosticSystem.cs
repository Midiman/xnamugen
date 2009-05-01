﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace xnaMugen.Diagnostics
{
    class DiagnosticSystem : SubSystem
    {
        public DiagnosticSystem(SubSystems subsystems)
            : base(subsystems)
        {
            m_form = new DiagnosticForm();
            m_formthread = new Thread(StartFormThread);
            m_lock = new Object();

			m_form.Closing += new System.ComponentModel.CancelEventHandler(m_form_Closing);
        }

		void m_form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			e.Cancel = true;

			m_form.Hide();
		}

        public override void Initialize()
        {
            InitializationSettings settings = GetSubSystem<InitializationSettings>();

            if (settings.ShowDiagnosticWindow == true || Debugger.IsAttached == true)
            {
                Start();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing == true)
            {
                Stop();
            }

            base.Dispose(disposing);
        }

        public void Start()
        {
            lock (m_lock)
            {
                if (m_formthread.IsAlive == false)
                {
                    m_formthread.Start();
                }
            }
        }

        public void Stop()
        {
            lock (m_lock)
            {
                if (m_formthread.IsAlive == true)
                {
                    ThreadStart func = StopFormThread;
                    m_form.Invoke(func);

                    m_formthread.Join();
                }
            }
        }

        public void Update()
        {
            lock (m_lock)
            {
                if (m_formthread.IsAlive == true)
                {
                    ThreadStart func = UpdateForm;
                    m_form.Invoke(func);
                }
            }
        }

        void StartFormThread()
        {
            Application.Run(m_form);
        }

        void StopFormThread()
        {
            Application.ExitThread();
        }

        void UpdateForm()
        {
            m_form.Set(GetMainSystem<Combat.FightEngine>());
        }

        #region Fields

        readonly DiagnosticForm m_form;

        readonly Thread m_formthread;

        readonly Object m_lock;

        #endregion
    }
}