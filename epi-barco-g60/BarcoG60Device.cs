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
		IInputHdmi1, IInputHdmi2, IInputHdmi4, IInputVga1,
		IBridgeAdvanced
	{
		// https://www.barco.com/en/support/g60-w10/docs
		// https://www.barco.com/services/website/en/TdeFiles/Download?FileNumber=R5910887&TdeType=1&Revision=01&ShowDownloadPage=False

		private bool HasLamps { get; set; }
		private bool HasScreen { get; set; }
		private bool HasLift { get; set; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="key"></param>
		/// <param name="name"></param>
		/// <param name="config"></param>
		/// <param name="comms"></param>
		public BarcoG60Controller(string key, string name, BarcoG60PropertiesConfig config, IBasicCommunication comms)
			: base(key, name)
		{
			var props = config;
			if (props == null)
			{
				Debug.Console(0, this, Debug.ErrorLogLevel.Error, "{0} configuration must be included", key);
				return;
			}

			ResetDebugLevels();

			Communication = comms;

			_receiveQueue = new GenericQueue(key + "-queue");

			PortGather = new CommunicationGather(Communication, GatherDelimiter);
			PortGather.LineReceived += PortGather_LineReceived;

			var socket = Communication as ISocketStatus;
			_isSerialComm = (socket == null);

			var pollIntervalMs = props.PollIntervalMs > 45000 ? props.PollIntervalMs : 45000;
			CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollIntervalMs, 180000, 300000,
				StatusGet);

			CommunicationMonitor.StatusChange += CommunicationMonitor_StatusChange;

			DeviceManager.AddDevice(CommunicationMonitor);

			WarmupTime = props.WarmingTimeMs > 10000 ? props.WarmingTimeMs : 15000;
			CooldownTime = props.CoolingTimeMs > 10000 ? props.CoolingTimeMs : 15000;

			HasLamps = props.HasLamps;
			HasScreen = props.HasScreen;
			HasLift = props.HasLift;

			InitializeInputs();
		}




		#region IBridgeAdvanced Members

		/// <summary>
		/// LinkToApi
		/// </summary>
		/// <param name="trilist"></param>
		/// <param name="joinStart"></param>
		/// <param name="joinMapKey"></param>
		/// <param name="bridge"></param>
		public void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
			//LinkDisplayToApi(this, trilist, joinStart, joinMapKey, bridge);
			//LinkDisplayCustomToApi(trilist, joinStart, joinMapKey, bridge);

			var joinMap = new BarcoG60BridgeJoinMap(joinStart);

			// This adds the join map to the collection on the bridge
			if (bridge != null)
			{
				bridge.AddJoinMap(Key, joinMap);
			}

			var customJoins = JoinMapHelper.TryGetJoinMapAdvancedForDevice(joinMapKey);
			if (customJoins != null)
			{
				joinMap.SetCustomJoinData(customJoins);
			}

			Debug.Console(0, this, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));
			Debug.Console(0, this, "Linking to Bridge Type {0}", GetType().Name);

			// links to bridge
			// device name
			trilist.SetString(joinMap.Name.JoinNumber, Name);

			//var twoWayDisplay = this as TwoWayDisplayBase;
			//trilist.SetBool(joinMap.IsTwoWayDisplay.JoinNumber, twoWayDisplay != null);

			// lamp, screen, lift config outputs
			trilist.SetBool(joinMap.HasLamps.JoinNumber, HasLamps);
			trilist.SetBool(joinMap.HasScreen.JoinNumber, HasScreen);
			trilist.SetBool(joinMap.HasLift.JoinNumber, HasLift);

			if (CommunicationMonitor != null)
			{
				CommunicationMonitor.IsOnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);
			}

			// power off
			trilist.SetSigTrueAction(joinMap.PowerOff.JoinNumber, PowerOff);
			PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);

			// power on 
			trilist.SetSigTrueAction(joinMap.PowerOn.JoinNumber, PowerOn);
			PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);

			// input (digital select, digital feedback, names)
			for (var i = 0; i < InputPorts.Count; i++)
			{
				var inputIndex = i;
				var input = InputPorts.ElementAt(inputIndex);

				if (input == null) continue;

				trilist.SetSigTrueAction((ushort)(joinMap.InputSelectOffset.JoinNumber + inputIndex), () =>
				{
					Debug.Console(DebugVerbose, this, "InputSelect Digital-'{0}'", inputIndex);
					SetInput = inputIndex;
				});

				trilist.StringInput[(ushort)(joinMap.InputNamesOffset.JoinNumber + inputIndex)].StringValue = input.Key;

				InputFeedback[inputIndex].LinkInputSig(trilist.BooleanInput[joinMap.InputSelectOffset.JoinNumber + (uint)inputIndex]);
			}

			// input (analog select)
			trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, analogValue =>
			{
				Debug.Console(DebugNotice, this, "InputSelect Analog-'{0}'", analogValue);
				SetInput = analogValue;
			});

			// input (analog feedback)
			CurrentInputNumberFeedback.LinkInputSig(trilist.UShortInput[joinMap.InputSelect.JoinNumber]);
			CurrentInputFeedback.OutputChange +=
				(sender, args) => Debug.Console(DebugNotice, this, "CurrentInputFeedback: {0}", args.StringValue);


			// bridge online change
			trilist.OnlineStatusChange += (sender, args) =>
			{
				if (!args.DeviceOnLine) return;

				// device name
				trilist.SetString(joinMap.Name.JoinNumber, Name);
				// lamp, screen, lift config outputs
				trilist.SetBool(joinMap.HasLamps.JoinNumber, HasLamps);
				trilist.SetBool(joinMap.HasScreen.JoinNumber, HasScreen);
				trilist.SetBool(joinMap.HasLift.JoinNumber, HasLift);

				PowerIsOnFeedback.FireUpdate();
				CurrentInputFeedback.FireUpdate();
				CurrentInputNumberFeedback.FireUpdate();

				for (var i = 0; i < InputPorts.Count; i++)
				{
					var inputIndex = i;
					InputFeedback[inputIndex].FireUpdate();
				}
			};
		}


		#endregion



		#region ICommunicationMonitor Members

		/// <summary>
		/// IBasicComminication object
		/// </summary>
		public IBasicCommunication Communication { get; private set; }

		/// <summary>
		/// Port gather object
		/// </summary>
		public CommunicationGather PortGather { get; private set; }

		/// <summary>
		/// Communication status monitor object
		/// </summary>
		public StatusMonitorBase CommunicationMonitor { get; private set; }

		private const string CmdDelimiter = "\x0D\x0A";
		private const string GatherDelimiter = "]";

		private readonly GenericQueue _receiveQueue;

		#endregion



		/// <summary>
		/// Initialize (override from PepperDash Essentials)
		/// </summary>
		public override void Initialize()
		{
			Communication.Connect();
			CommunicationMonitor.Start();
		}

		///// <summary>
		///// Custom activate
		///// </summary>
		///// <returns></returns>
		//public override bool CustomActivate()
		//{
		//    Communication.Connect();
		//    CommunicationMonitor.Start();

		//    return base.CustomActivate();
		//}

		private void CommunicationMonitor_StatusChange(object sender, MonitorStatusChangeEventArgs args)
		{
			CommunicationMonitor.IsOnlineFeedback.FireUpdate();
		}

		private void PortGather_LineReceived(object sender, GenericCommMethodReceiveTextArgs args)
		{
			if (args == null)
			{
				Debug.Console(DebugNotice, this, "PortGather_LineReceived: args are null");
				return;
			}

			if (string.IsNullOrEmpty(args.Text))
			{
				Debug.Console(DebugNotice, this, "PortGather_LineReceived: args.Text is null or empty");
				return;
			}

			try
			{
				Debug.Console(DebugVerbose, this, "PortGather_LineReceived: args.Text-'{0}'", args.Text);
				_receiveQueue.Enqueue(new ProcessStringMessage(args.Text, ProcessResponse));
			}
			catch (Exception ex)
			{
				Debug.Console(DebugNotice, this, Debug.ErrorLogLevel.Error, "HandleLineReceived Exception Message: {0}", ex.Message);
				Debug.Console(DebugVerbose, this, Debug.ErrorLogLevel.Error, "HandleLineRecieved Exception Stack Trace: {0}", ex.StackTrace);
				if (ex.InnerException != null) Debug.Console(DebugNotice, this, Debug.ErrorLogLevel.Error, "HandleLineReceived Inner Exception: '{0}'", ex.InnerException);
			}
		}

		private void ProcessResponse(string response)
		{
			// ex poll tx/rx
			// Tx: "[DPON0]\x0D\x0A"
			// Rx: "[DPON0]\x0D\x0A"			

			Debug.Console(DebugNotice, this, "ProcessResponse: {0}", response);

			// https://stackoverflow.com/questions/7175580/use-string-contains-with-switch
			//string[] responseTypes = new string[]
			//{
			//    "DPON",     // index-0: screen - direct power
			//    "SBPM",     // index-1: screen - standby power mode
			//    "MSRC",     // index-2: screen - PIP-PBP - main source
			//    "ASPR",     // index-3: screen - aspect ratio
			//    "LSHS",     // index-4: light source - light source info - LD hours
			//};

			var responseData = response.Trim('[').Split('!');
			var responseType = string.IsNullOrEmpty(responseData[0]) ? "" : responseData[0];
			var responseValue = string.IsNullOrEmpty(responseData[1]) ? "" : responseData[1];

			Debug.Console(DebugVerbose, this, "ProcessorResponse: responseType-'{0}', responseValue-'{1}'", responseType, responseValue);

			switch (responseType)
			{
				case "POWR":
					{
						PowerIsOn = responseValue.Contains("1");
						break;
					}
				case "DPON":
					{
						PowerIsOn = responseValue.Contains("1");
						break;
					}
				case "SBPM":
					{
						PowerIsOn = responseValue.Contains("1");
						break;
					}
				case "MSRC":
					{
						UpdateInputFb(responseValue);
						break;
					}
				case "ASPR":
					{
						Debug.Console(DebugVerbose, this, "ProcessRespopnse: aspect ratio response '{0}' not tracked", responseType);
						break;
					}
				case "LSHS":
					{
						Debug.Console(DebugVerbose, this, "ProcessRespopnse: light source response '{0}' not tracked", responseType);
						break;
					}
				default:
					{
						Debug.Console(DebugVerbose, this, "ProcessRespopnse: unknown response '{0}'", responseType);
						break;
					}
			}
		}

		public void SendText(string cmd)
		{
			if (string.IsNullOrEmpty(cmd)) return;

			// tx format: "[{cmd}{value}]"
			Communication.SendText(string.Format("[{0}]", cmd));
		}

		// formats outgoing message
		private void SendText(string cmd, string value)
		{
			if (string.IsNullOrEmpty(cmd)) return;

			// tx format: "[{cmd}{value}]"
			Communication.SendText(string.Format("[{0}{1}]", cmd, string.IsNullOrEmpty(value) ? "?" : value));
		}

		// formats outgoing message
		private void SendText(string cmd, int value)
		{
			if (string.IsNullOrEmpty(cmd)) return;

			// tx format: "[{cmd}{value}]"
			Communication.SendText(string.Format("[{0}{1}]", cmd, value.ToString(CultureInfo.InvariantCulture)));
		}

		/// <summary>
		/// Executes a switch, turning on display if necessary.
		/// </summary>
		/// <param name="selector"></param>
		public override void ExecuteSwitch(object selector)
		{
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
					if (IsWarmingUp) return;

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

		/// <summary>
		/// Input power on constant
		/// </summary>
		public const int InputPowerOn = 101;

		/// <summary>
		/// Input power off constant
		/// </summary>
		public const int InputPowerOff = 102;

		/// <summary>
		/// Input key list
		/// </summary>
		public static List<string> InputKeys = new List<string>();

		/// <summary>
		/// Input (digital) feedback
		/// </summary>
		public List<BoolFeedback> InputFeedback;

		/// <summary>
		/// Input number (analog) feedback
		/// </summary>
		public IntFeedback CurrentInputNumberFeedback;

		private RoutingInputPort _currentInputPort;
		private List<bool> _inputFeedback;
		private int _currentInputNumber;

		/// <summary>
		/// Input number property
		/// </summary>
		public int CurrentInputNumber
		{
			get { return _currentInputNumber; }
			private set
			{
				if (_currentInputNumber == value) return;

				_currentInputNumber = value;
				CurrentInputNumberFeedback.FireUpdate();
				UpdateBooleanFeedback(value);
			}
		}

		/// <summary>
		/// Sets the requested input
		/// </summary>
		public int SetInput
		{
			set
			{
				if (value <= 0 || value >= InputPorts.Count) return;

				Debug.Console(DebugNotice, this, "SetInput: value-'{0}'", value);

				// -1 to get actual input in list after 0d check
				var port = GetInputPort(value - 1);
				if (port == null)
				{
					Debug.Console(DebugNotice, this, "SetInput: failed to get input port");
					return;
				}

				Debug.Console(DebugVerbose, this, "SetInput: port.key-'{0}', port.Selector-'{1}', port.ConnectionType-'{2}', port.FeebackMatchObject-'{3}'",
					port.Key, port.Selector, port.ConnectionType, port.FeedbackMatchObject);

				ExecuteSwitch(port.Selector);
			}

		}

		private RoutingInputPort GetInputPort(int input)
		{
			return InputPorts.ElementAt(input);
		}

		private void AddRoutingInputPort(RoutingInputPort port, string fbMatch)
		{
			port.FeedbackMatchObject = fbMatch;
			InputPorts.Add(port);
		}

		protected override Func<string> CurrentInputFeedbackFunc
		{
			get { return () => _currentInputPort.Key; }
		}

		private void InitializeInputs()
		{
			// TODO [ ] verify feedback match value ** last parameter of AddRoutingInputPort - string **
			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi1), this), "01");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn2, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi2), this), "02");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.DviIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Dvi, new Action(InputDvi1), this), "03");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.VgaIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Vga, new Action(InputVga1), this), "00");

			// RoutingPortNames does not contain and SDI, using HdmiIn5
			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn5, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Sdi, new Action(InputSdi1), this), "05");

			// RoutingPortNames does not contain and HD-BaseT, using HdmiIn4
			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn4, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Streaming, new Action(InputHdBaseT1), this), "04");

			// initialize feedbacks after adding input ports
			_inputFeedback = new List<bool>();
			InputFeedback = new List<BoolFeedback>();

			for (var i = 0; i < InputPorts.Count; i++)
			{
				var input = i + 1;
				InputFeedback.Add(new BoolFeedback(() => CurrentInputNumber == input));
			}

			CurrentInputNumberFeedback = new IntFeedback(() =>
			{
				Debug.Console(DebugVerbose, this, "InputNumberFeedback: CurrentInputNumber-'{0}'", CurrentInputNumber);
				return CurrentInputNumber;
			});
		}

		/// <summary>
		/// Lists available input routing ports
		/// </summary>
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
			SendText("MSRC", 1);
		}

		/// <summary>
		/// Select Hdmi 2
		/// </summary>
		public void InputHdmi2()
		{
			// TODO [ ] verify input selection commands
			SendText("MSRC", 2);
		}

		/// <summary>
		/// Hdmi 4 >> HD-Base-T
		/// </summary>
		public void InputHdmi4()
		{
			InputHdBaseT1();
		}

		/// <summary>
		/// Hdmi 5 >> SDI
		/// </summary>
		public void InputHdmi5()
		{
			InputSdi1();
		}

		/// <summary>
		/// Select DVI 1 Input (AV)
		/// </summary>
		public void InputDvi1()
		{
			// TODO [ ] verify input selection commands
			SendText("MSRC", 3);
		}

		/// <summary>
		/// Select HDBase-T 1
		/// </summary>
		public void InputHdBaseT1()
		{
			// TODO [ ] verify input selection commands
			SendText("MSRC", 4);
		}

		/// <summary>
		/// Select VGA 1
		/// </summary>
		public void InputVga1()
		{
			// TODO [ ] verify input selection commands
			SendText("MSRC", 0);
		}

		/// <summary>
		/// Select SDI 1
		/// </summary>
		public void InputSdi1()
		{
			// TODO [ ] verify input selection commands
			SendText("MSRC", 5);
		}

		/// <summary>
		/// Toggles the display input
		/// </summary>
		public void InputToggle()
		{
			// TODO [ ] Fill in input recall command and values
			//SendText("", "");

			throw new NotImplementedException();
		}

		/// <summary>
		/// Poll input
		/// </summary>
		public void InputGet()
		{
			// TODO [ ] verify input poll command
			SendText("MSRC", "?");
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
				Debug.Console(DebugNotice, this, "UpdateInputFb: _currentInputPort-'{0}' == newInput-'{1')", _currentInputPort.Key, newInput.Key);
				return;
			}

			Debug.Console(DebugNotice, this, "UpdateInputFb: newInput key-'{0}', connectionType-'{1}', feedbackMatchObject-'{2}'",
				newInput.Key, newInput.ConnectionType, newInput.FeedbackMatchObject);

			_currentInputPort = newInput;
			CurrentInputFeedback.FireUpdate();

			var key = newInput.Key;
			Debug.Console(DebugNotice, this, "UpdateInputFb: key-'{0}'", key);
			// TODO [ ] Update input values to align with API
			switch (key)
			{
				case "hdmiIn1":
					CurrentInputNumber = 1;
					break;
				case "hdmiIn2":
					CurrentInputNumber = 2;
					break;
				case "InputDvi1":
					CurrentInputNumber = 3;
					break;
				case "vga1":
					CurrentInputNumber = 4;
					break;
				case "hdbaseT1":
					CurrentInputNumber = 5;
					break;
				case "sdi1":
					CurrentInputNumber = 6;
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

		private bool _isCoolingDown;
		private bool _isSerialComm;
		private bool _isWarmingUp;
		private bool _powerIsOn;


		/// <summary>
		/// Power is on property
		/// </summary>
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

		/// <summary>
		/// Is warming property
		/// </summary>
		public bool IsWarmingUp
		{
			get { return _isWarmingUp; }
			set
			{
				_isWarmingUp = value;
				IsWarmingUpFeedback.FireUpdate();
			}
		}

		/// <summary>
		/// Is cooling property
		/// </summary>
		public bool IsCoolingDown
		{
			get { return _isCoolingDown; }
			set
			{
				_isCoolingDown = value;
				IsCoolingDownFeedback.FireUpdate();
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

		/// <summary>
		/// Set Power On For Device
		/// </summary>
		public override void PowerOn()
		{
			if (_isWarmingUp || _isCoolingDown) return;

			SendText("POWR", 1);		// power on, alternate cmds: "SBPM", "DPON"
			//PowerIsOn = true;
		}

		/// <summary>
		/// Set Power Off for Device
		/// </summary>
		public override void PowerOff()
		{
			if (_isWarmingUp || _isCoolingDown) return;

			SendText("POWR", 0);		// power off, alternate cmds: "SBPM", "DPON" 
			//PowerIsOn = false;
		}

		/// <summary>
		/// Poll Power
		/// </summary>
		public void PowerGet()
		{
			SendText("POWR", "?");		// power get, alternate cmds: "SBPM", "DPON"
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



		#region DebugLevels

		private uint DebugTrace { get; set; }
		private uint DebugNotice { get; set; }
		private uint DebugVerbose { get; set; }


		public void ResetDebugLevels()
		{
			DebugTrace = 0;
			DebugNotice = 1;
			DebugVerbose = 2;
		}

		public void SetDebugLevels(uint level)
		{
			DebugTrace = level;
			DebugNotice = level;
			DebugVerbose = level;
		}

		#endregion
	}
}