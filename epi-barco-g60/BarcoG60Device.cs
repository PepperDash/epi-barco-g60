using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.Queues;
using PepperDash.Essentials.Core.Routing;
using PepperDash.Essentials.Devices.Displays;

namespace Plugin.BarcoG60
{
	public class BarcoG60Controller : TwoWayDisplayBase, ICommunicationMonitor, 
		IInputHdmi1, IInputHdmi2, IInputVga1,
		IBridgeAdvanced
	{
        // https://www.barco.com/en/support/g60-w10/docs
        // https://www.barco.com/services/website/en/TdeFiles/Download?FileNumber=R5910887&TdeType=1&Revision=01&ShowDownloadPage=False

	    private const int DebugLevel1 = 0;
	    private const int DebugLevel2 = 0;

        private const string CmdDelimiter = "\x0D\x0A";
	    private const string GatherDelimiter = "\x0D";

	    GenericQueue ReceiveQueue;

		public const int InputPowerOn = 101;
		public const int InputPowerOff = 102;

		public static List<string> InputKeys = new List<string>();
		public List<BoolFeedback> InputFeedback;
		public IntFeedback InputNumberFeedback;
		private RoutingInputPort _currentInputPort;
		private List<bool> _inputFeedback;
		private int _inputNumber;

		private bool _isCoolingDown;
		private bool _isSerialComm;
		private bool _isWarmingUp;
		private bool _powerIsOn;

		public BarcoG60Controller(string key, string name, BarcoG60PropertiesConfig config, IBasicCommunication comms)
			: base(key, name)
		{
			Communication = comms;

			ReceiveQueue = new GenericQueue(key + "-queue");

			var props = config;
			if (props == null)
			{
				Debug.Console(0, this, Debug.ErrorLogLevel.Error, "{0} configuration must be included", key);
				return;
			}
			_pollIntervalMs = props.PollIntervalMs > 45000 ? props.PollIntervalMs : 45000;
			_coolingTimeMs = props.CoolingTimeMs > 10000 ? props.CoolingTimeMs : 15000;
			_warmingTimeMs = props.WarmingTimeMs > 10000 ? props.WarmingTimeMs : 15000;

			InputNumberFeedback = new IntFeedback(() => _inputNumber);

			Init();
		}

		public IBasicCommunication Communication { get; private set; }
		public CommunicationGather PortGather { get; private set; }		

		public bool PowerIsOn
		{
			get { return _powerIsOn; }
			set
			{
				if (_powerIsOn == value)
				{
					return;
				}

				_powerIsOn = value;

				if (_powerIsOn)
				{
					IsWarmingUp = true;

					WarmupTimer = new CTimer(o =>
					{
						IsWarmingUp = false;
					}, WarmupTime);
				}
				else
				{
					IsCoolingDown = true;

					CooldownTimer = new CTimer(o =>
					{
						IsCoolingDown = false;
					}, CooldownTime);
				}

				PowerIsOnFeedback.FireUpdate();
			}
		}

		public bool IsWarmingUp
		{
			get { return _isWarmingUp; }
			set
			{
				_isWarmingUp = value;
				IsWarmingUpFeedback.FireUpdate();
			}
		}

		public bool IsCoolingDown
		{
			get { return _isCoolingDown; }
			set
			{
				_isCoolingDown = value;
				IsCoolingDownFeedback.FireUpdate();
			}
		}

		private readonly uint _coolingTimeMs;
		private readonly uint _warmingTimeMs;
		private readonly long _pollIntervalMs;

		public int InputNumber
		{
			get { return _inputNumber; }
			private set
			{
				if (_inputNumber == value) return;

				_inputNumber = value;
				InputNumberFeedback.FireUpdate();
				UpdateBooleanFeedback(value);
			}
		}

		protected override Func<bool> PowerIsOnFeedbackFunc
		{
			get { return () => PowerIsOn; }
		}

		protected override Func<bool> IsCoolingDownFeedbackFunc
		{
			get { return () => IsCoolingDown; }
		}

		protected override Func<bool> IsWarmingUpFeedbackFunc
		{
			get { return () => IsWarmingUp; }
		}

		protected override Func<string> CurrentInputFeedbackFunc
		{
			get { return () => _currentInputPort.Key; }
		}		

		#region IBridgeAdvanced Members

		public void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
			LinkDisplayToApi(this, trilist, joinStart, joinMapKey, bridge);
		}

		#endregion

		#region ICommunicationMonitor Members

		public StatusMonitorBase CommunicationMonitor { get; private set; }

		#endregion

		private void Init()
		{
			WarmupTime = _warmingTimeMs > 0 ? _warmingTimeMs : 15000;
			CooldownTime = _coolingTimeMs > 0 ? _coolingTimeMs : 15000;

			_inputFeedback = new List<bool>();
			InputFeedback = new List<BoolFeedback>();

            PortGather = new CommunicationGather(Communication, GatherDelimiter);
			PortGather.LineReceived += PortGather_LineReceived;

			var socket = Communication as ISocketStatus;
			if (socket != null)
			{
				//This Instance Uses IP Control
			    _isSerialComm = false;
			}
			else
			{
				// This instance uses RS-232 Control
				_isSerialComm = true;
			}

			var pollInterval = _pollIntervalMs > 0 ? _pollIntervalMs : 45000;
			CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollInterval, 180000, 300000,
				StatusGet);
			CommunicationMonitor.StatusChange += CommunicationMonitor_StatusChange;

			DeviceManager.AddDevice(CommunicationMonitor);

            // TODO [ ] verify feedback match value ** last parameter of AddRoutingInputPort - string **
			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi1), this), "1");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn2, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi2), this), "2");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.DviIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Dvi, new Action(InputDvi1), this), "3");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.VgaIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Vga, new Action(InputVga1), this), "0");

            AddRoutingInputPort(
                new RoutingInputPort("SDI", eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Sdi, new Action(InputSdi1), this), "5");

            AddRoutingInputPort(
                new RoutingInputPort("HDBase-T", eRoutingSignalType.Audio | eRoutingSignalType.Video,
                    eRoutingPortConnectionType.Streaming, new Action(InputHdBaseT1), this), "4");
		}

		public override bool CustomActivate()
		{
			Communication.Connect();

			if (_isSerialComm)
			{
				CommunicationMonitor.Start();
			}

			return base.CustomActivate();
		}

		private void CommunicationMonitor_StatusChange(object sender, MonitorStatusChangeEventArgs e)
		{
			CommunicationMonitor.IsOnlineFeedback.FireUpdate();
		}

		private void PortGather_LineReceived(object sender, GenericCommMethodReceiveTextArgs args)
		{
			ReceiveQueue.Enqueue(new ProcessStringMessage(args.Text, ProcessResponse));
		}

		private void ProcessResponse(string response)
		{
			// ex poll tx/rx
			// Tx: "[DPON0]\x0D\x0A"
			// Rx: "[DPON0]\x0D\x0A"			

			Debug.Console(DebugLevel1, this, "ProcessResponse: {0}", response);

            // https://stackoverflow.com/questions/7175580/use-string-contains-with-switch
            string[] responseTypes = new string[]
		    {
                "DPON",     // index-0: screen - direct power
                "SBPM",     // index-1: screen - standby power mode
                "MSRC",     // index-2: screen - PIP-PBP - main source
                "ASPR",     // index-3: screen - aspect ratio
                "LSHS",     // index-4: light source - light source info - LD hours
		    };

		    var responseRecieved = responseTypes.FirstOrDefault(response.Contains);
            Debug.Console(DebugLevel2, this, "ProcessorResponse: responseRecieved-'{0}'", responseRecieved);

			switch (responseRecieved)
			{
				case "DPON":
			    {
			        UpdatePowerFb(responseRecieved.Contains("DPON1") ? "1" : "0");
			        break;
			    }
			    case "SBPM":
					{
                        UpdatePowerFb(responseRecieved.Contains("SBPM") ? "1" : "0");
						break;
					}
                case "MSRC":
			    {
                    UpdateInputFb(responseRecieved.Substring(responseTypes[2].Length + 1, 1));						
			        break;
			    }
                case"ASPR":
			    {
                    Debug.Console(DebugLevel2, this, "ProcessRespopnse: aspect ratio response '{0}' not tracked", responseRecieved);
			        break;
			    }
                case "LSHS":
			    {
                    Debug.Console(DebugLevel2, this, "ProcessRespopnse: light source response '{0}' not tracked", responseRecieved);
			        break;
			    }
                default:
			    {
                    Debug.Console(DebugLevel2, this, "ProcessRespopnse: unknown response '{0}'", responseRecieved);
			        break;
			    }
			}
		}


		/// <summary>
		/// Formats an outgoing message
		/// </summary>
		/// <param name="cmd"></param>
		/// <param name="value">a string</param>
		private void SendData(string cmd, string value)
		{
			if (string.IsNullOrEmpty(cmd)) return;
			
            // tx format: "[{cmd}{value}]\n"
		    Communication.SendText(string.Format("[{0}{1}]{2}", cmd, string.IsNullOrEmpty(value)? "?": value, CmdDelimiter));
		}

        /// <summary>
        /// Formats an outgoing message
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="value">a number</param>
        private void SendData(string cmd, int value)
		{
			if (string.IsNullOrEmpty(cmd)) return;

			// tx format: "[{cmd}{value}]\n"
            Communication.SendText(string.Format("[{0}{1}]{2}", cmd, value == 0 ? "?" : value.ToString(CultureInfo.InvariantCulture), CmdDelimiter));
		}

		/// <summary>
		/// Executes a switch, turning on display if necessary.
		/// </summary>
		/// <param name="selector"></param>
		public override void ExecuteSwitch(object selector)
		{
			//if (!(selector is Action))
			//    Debug.Console(DebugLevel1, this, "WARNING: ExecuteSwitch cannot handle type {0}", selector.GetType());

			if (PowerIsOn)
			{
				var action = selector as Action;
				if (action != null)
				{
					action();
				}
			}
			else // if power is off, wait until we get on FB to send it. 
			{
				// One-time event handler to wait for power on before executing switch
				EventHandler<FeedbackEventArgs> handler = null; // necessary to allow reference inside lambda to handler
				handler = (o, a) =>
				{
					if (_isWarmingUp)
					{
						return;
					}

					IsWarmingUpFeedback.OutputChange -= handler;

					var action = selector as Action;
					if (action != null)
					{
						action();
					}
				};
				IsWarmingUpFeedback.OutputChange += handler; // attach and wait for on FB
				PowerOn();
			}
		}		

		#region Inputs

		private void AddRoutingInputPort(RoutingInputPort port, string fbMatch)
		{
			port.FeedbackMatchObject = fbMatch;
			InputPorts.Add(port);
		}

		public void ListRoutingInputPorts()
		{
			foreach (var inputPort in InputPorts)
			{
				Debug.Console(0, this, "ListRoutingInputPorts: key-'{0}', connectionType-'{1}', feedbackMatchObject-'{2}'",
					inputPort.Key, inputPort.ConnectionType, inputPort.FeedbackMatchObject);
			}
		}

		/// <summary>
		/// Select Hdmi 1
		/// </summary>
		public void InputHdmi1()
		{
            // TODO [ ] verify input selection commands
			SendData("MSRC", 1);
		}

		/// <summary>
		/// Select Hdmi 2
		/// </summary>
		public void InputHdmi2()
		{
            // TODO [ ] verify input selection commands
            SendData("MSRC", 2);
		}
		
		/// <summary>
		/// Select DVI 1 Input (AV)
		/// </summary>
		public void InputDvi1()
		{
            // TODO [ ] verify input selection commands
            SendData("MSRC", 3);
		}

		/// <summary>
		/// Select HDBase-T 1
		/// </summary>
		public void InputHdBaseT1()
		{
            // TODO [ ] verify input selection commands
            SendData("MSRC", 4);
		}

		/// <summary>
		/// Select VGA 1
		/// </summary>
		public void InputVga1()
		{
            // TODO [ ] verify input selection commands
            SendData("MSRC", 0);
		}

        /// <summary>
        /// Select SDI 1
        /// </summary>
	    public void InputSdi1()
	    {
            // TODO [ ] verify input selection commands
            SendData("MSRC", 5);
	    }

		/// <summary>
		/// Toggles the display input
		/// </summary>
		public void InputToggle()
		{
            // TODO [ ] Fill in input recall command and values
            //SendData("", "");

            throw new NotImplementedException();
		}

		/// <summary>
		/// Poll input
		/// </summary>
		public void InputGet()
		{
            // TODO [ ] verify input poll command
            SendData("MSRC", "?");
		}

		/// <summary>
		/// Process Input Feedback from Response
		/// </summary>
		/// <param name="s">response from device</param>
		public void UpdateInputFb(string s)
		{
			var newInput = InputPorts.FirstOrDefault(i => i.FeedbackMatchObject.Equals(s.ToLower()));
			if (newInput == null) return;
			if (newInput == _currentInputPort)
			{
				Debug.Console(DebugLevel1, this, "UpdateInputFb: _currentInputPort-'{0}' == newInput-'{1')", _currentInputPort.Key, newInput.Key);
				return;
			}

            Debug.Console(DebugLevel1, this, "UpdateInputFb: newInput key-'{0}', connectionType-'{1}', feedbackMatchObject-'{2}'",
				newInput.Key, newInput.ConnectionType, newInput.FeedbackMatchObject);

			_currentInputPort = newInput;
			CurrentInputFeedback.FireUpdate();

			var key = newInput.Key;
			Debug.Console(DebugLevel1, this, "UpdateInputFb: key-'{0}'", key);
            // TODO [ ] Update input values to align with API
			switch (key)
			{
				case "hdmiIn1":
					InputNumber = 1;
					break;
				case "hdmiIn2":
					InputNumber = 2;
					break;
				case "InputDvi1":
					InputNumber = 3;
					break;
				case "vga1":
					InputNumber = 4;
					break;
                case "hdbaseT1":
			        InputNumber = 5;
			        break;
                case "sdi1":
			        InputNumber = 6;
			        break;
			}
		}

		/// <summary>
		/// Updates Digital Route Feedback for Simpl EISC
		/// </summary>
		/// <param name="data">currently routed source</param>
		private void UpdateBooleanFeedback(int data)
		{
			try
			{
				if (_inputFeedback[data])
				{
					return;
				}

				for (var i = 1; i < InputPorts.Count + 1; i++)
				{
					_inputFeedback[i] = false;
				}

				_inputFeedback[data] = true;
				foreach (var item in InputFeedback)
				{
					var update = item;
					update.FireUpdate();
				}
			}
			catch (Exception e)
			{
				Debug.Console(0, this, "{0}", e.Message);
			}
		}

		#endregion

		#region Power

		/// <summary>
		/// Set Power On For Device
		/// </summary>
		public override void PowerOn()
		{
			SendData("SBPM", "1");      // standby power mode
            //SendData("DPON", "1");    // direct power on
		}

		/// <summary>
		/// Set Power Off for Device
		/// </summary>
		public override void PowerOff()
		{
            SendData("SBPM", "0");      // standby power mode
			//SendData("DPON", "0");    // direct power on
		}

		/// <summary>
		/// Poll Power
		/// </summary>
		public void PowerGet()
		{
            SendData("SBPM", "?");      // standby power mode
            //SendData("DPON", "?");    // direct power on
		}


		/// <summary>
		/// Toggle current power state for device
		/// </summary>
		public override void PowerToggle()
		{
			if (PowerIsOn)
			{
				PowerOff();
			}
			else
			{
				PowerOn();
			}
		}

		/// <summary>
		/// Process Power Feedback from Response
		/// </summary>
		/// <param name="s">response from device</param>
		public void UpdatePowerFb(string s)
		{
			PowerIsOn = s.Contains("1");
		}

		#endregion

		/// <summary>
		/// Starts the Poll Ring
		/// </summary>
		public void StatusGet()
		{
			CrestronInvoke.BeginInvoke((o) =>
			{
				PowerGet();
				CrestronEnvironment.Sleep(2000);
				InputGet();
			});
		}
	}
}