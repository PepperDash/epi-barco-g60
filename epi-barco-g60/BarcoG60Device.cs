using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.CrestronThread;
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

		private bool _isSerialComm;
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

			LampHoursFeedback = new IntFeedback(() => LampHours);

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

			WarmupTime = props.WarmingTimeMs > 30000 ? props.WarmingTimeMs : 30000;
			CooldownTime = props.CoolingTimeMs > 30000 ? props.CoolingTimeMs : 30000;

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

			// power off & is cooling
			trilist.SetSigTrueAction(joinMap.PowerOff.JoinNumber, PowerOff);
			PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);
			IsCoolingDownFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsCooling.JoinNumber]);

			// power on & is warming
			trilist.SetSigTrueAction(joinMap.PowerOn.JoinNumber, PowerOn);
			PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);
			IsWarmingUpFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsWarming.JoinNumber]);

			// input (digital select, digital feedback, names)
			for (var i = 0; i < InputPorts.Count; i++)
			{
				var inputIndex = i;
				var input = InputPorts.ElementAt(inputIndex);

				if (input == null) continue;

				trilist.SetSigTrueAction((ushort)(joinMap.InputSelectOffset.JoinNumber + inputIndex), () =>
				{
					Debug.Console(DebugVerbose, this, "InputSelect Digital-'{0}'", inputIndex + 1);
					SetInput = inputIndex + 1;
				});

				trilist.StringInput[(ushort)(joinMap.InputNamesOffset.JoinNumber + inputIndex)].StringValue = string.IsNullOrEmpty(input.Key) ? string.Empty : input.Key;

				InputFeedback[inputIndex].LinkInputSig(trilist.BooleanInput[joinMap.InputSelectOffset.JoinNumber + (uint)inputIndex]);
			}

			// input (analog select)
			trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, analogValue =>
			{
				Debug.Console(DebugNotice, this, "InputSelect Analog-'{0}'", analogValue);
				SetInput = analogValue;
			});

			// input (analog feedback)
			if (CurrentInputNumberFeedback != null)
				CurrentInputNumberFeedback.LinkInputSig(trilist.UShortInput[joinMap.InputSelect.JoinNumber]);

			if (CurrentInputFeedback != null)
				CurrentInputFeedback.OutputChange += (sender, args) => Debug.Console(DebugNotice, this, "CurrentInputFeedback: {0}", args.StringValue);

			// lamp hours feeback
			LampHoursFeedback.LinkInputSig(trilist.UShortInput[joinMap.LampHours.JoinNumber]);


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

				if (CurrentInputFeedback != null)
					CurrentInputFeedback.FireUpdate();
				if (CurrentInputNumberFeedback != null)
					CurrentInputNumberFeedback.FireUpdate();

				for (var i = 0; i < InputPorts.Count; i++)
				{
					var inputIndex = i;
					if (InputFeedback != null)
						InputFeedback[inputIndex].FireUpdate();
				}

				LampHoursFeedback.FireUpdate();
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
				Debug.Console(DebugNotice, this, Debug.ErrorLogLevel.Error, "HandleLineReceived Exception: {0}", ex.Message);
				Debug.Console(DebugVerbose, this, Debug.ErrorLogLevel.Error, "HandleLineRecieved StackTrace: {0}", ex.StackTrace);
				if (ex.InnerException != null) Debug.Console(DebugNotice, this, Debug.ErrorLogLevel.Error, "HandleLineReceived InnerException: '{0}'", ex.InnerException);
			}
		}

		private void ProcessResponse(string response)
		{
			if (string.IsNullOrEmpty(response)) return;

			Debug.Console(DebugNotice, this, "ProcessResponse: {0}", response);

			if (!response.Contains("!") || response.Contains("ERR"))
			{
				Debug.Console(DebugVerbose, this, "ProcessResponse: '{0}' is not tracked", response);
				return;
			}

			var responseData = response.Trim('[').Split('!');
			var responseType = string.IsNullOrEmpty(responseData[0]) ? "" : responseData[0];
			var responseValue = string.IsNullOrEmpty(responseData[1]) ? "" : responseData[1];

			Debug.Console(DebugVerbose, this, "ProcessResponse: responseType-'{0}', responseValue-'{1}'", responseType, responseValue);

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
						LampHours = Convert.ToInt16(responseValue);
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
			if (!Communication.IsConnected)
			{
				Debug.Console(DebugNotice, this, "SendText: device {0} connected", Communication.IsConnected ? "is" : "is not");
				return;
			}

			if (string.IsNullOrEmpty(cmd)) return;

			// tx format: "[{cmd}{value}]"
			var text = string.Format("[{0}]", cmd);
			Debug.Console(DebugNotice, this, "SendText: {0}", text);
			Communication.SendText(text);
		}

		// formats outgoing message
		private void SendText(string cmd, string value)
		{
			// tx format: "[{cmd}{value}]"
			SendText(string.Format("{0}{1}", cmd, string.IsNullOrEmpty(value) ? "?" : value));
		}

		// formats outgoing message
		private void SendText(string cmd, int value)
		{
			// tx format: "[{cmd}{value}]"
			SendText(string.Format("{0}{1}", cmd, value));
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
				Debug.Console(0, this, "ExecuteSwitch: action is {0}", action == null ? "null" : "not null");
				if (action != null)
				{
					CrestronInvoke.BeginInvoke(o => action());
				}
			}
			else // if power is off, wait until we get on FB to send it. 
			{
				// One-time event handler to wait for power on before executing switch
				EventHandler<FeedbackEventArgs> handler = null; // necessary to allow reference inside lambda to handler
				handler = (sender, args) =>
				{
					if (IsWarmingUp) return;

					IsWarmingUpFeedback.OutputChange -= handler;

					var action = selector as Action;
					Debug.Console(0, this, "ExecuteSwitch: action is {0}", action == null ? "null" : "not null");
					if (action != null)
					{
						CrestronInvoke.BeginInvoke(o => action());
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

		protected override Func<string> CurrentInputFeedbackFunc
		{
			get { return () => _currentInputPort != null ? _currentInputPort.Key : string.Empty; }
		}

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
				_currentInputNumber = value;
				CurrentInputNumberFeedback.FireUpdate();
				UpdateInputBooleanFeedback();
			}
		}

		/// <summary>
		/// Sets the requested input
		/// </summary>
		public int SetInput
		{
			set
			{
				if (value <= 0 || value > InputPorts.Count)
				{
					Debug.Console(DebugNotice, this, "SetInput: value-'{0}' is out of range (1 - {1})", value, InputPorts.Count);
					return;
				}

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

		private void InitializeInputs()
		{
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
					eRoutingPortConnectionType.Sdi, new Action(InputHdmi5), this), "05");

			// RoutingPortNames does not contain and HD-BaseT, using HdmiIn4
			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn4, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Streaming, new Action(InputHdmi4), this), "04");

			// initialize feedbacks after adding input ports
			_inputFeedback = new List<bool>();
			InputFeedback = new List<BoolFeedback>();

			for (var i = 0; i < InputPorts.Count; i++)
			{
				var input = i + 1;
				InputFeedback.Add(new BoolFeedback(() =>
				{
					Debug.Console(DebugNotice, this, "CurrentInput Number: {0}; input: {1};", CurrentInputNumber, input);
					return CurrentInputNumber == input;
				}));
			}

			CurrentInputNumberFeedback = new IntFeedback(() =>
			{
				Debug.Console(DebugVerbose, this, "CurrentInputNumberFeedback: {0}", CurrentInputNumber);
				return CurrentInputNumber;
			});
		}

		/// <summary>
		/// Lists available input routing ports
		/// </summary>
		public void ListRoutingInputPorts()
		{
			var index = 0;
			foreach (var inputPort in InputPorts)
			{
				Debug.Console(0, this, "ListRoutingInputPorts: index-'{0}' key-'{1}', connectionType-'{2}', feedbackMatchObject-'{3}'",
					index, inputPort.Key, inputPort.ConnectionType, inputPort.FeedbackMatchObject);
				index++;
			}
		}

		/// <summary>
		/// Select Hdmi 1
		/// </summary>
		public void InputHdmi1()
		{
			Debug.Console(DebugVerbose, this, "InputHdmi1 executing...");
			SendText("MSRC", 1);
			Thread.Sleep(2000);
			SendText("MSRC", "?");
		}

		/// <summary>
		/// Select Hdmi 2
		/// </summary>
		public void InputHdmi2()
		{
			Debug.Console(DebugVerbose, this, "InputHdmi2 executing...");
			SendText("MSRC", 2);
			Thread.Sleep(2000);
			SendText("MSRC", "?");
		}

		/// <summary>
		/// Hdmi 4 >> HD-Base-T
		/// </summary>
		public void InputHdmi4()
		{
			Debug.Console(DebugVerbose, this, "InputHdmi4 (HD-BaseT) executing...");
			SendText("MSRC", 4);
			Thread.Sleep(2000);
			SendText("MSRC", "?");
		}

		/// <summary>
		/// Hdmi 5 >> SDI
		/// </summary>
		public void InputHdmi5()
		{
			Debug.Console(DebugVerbose, this, "InputHdmi5 (SDI) executing...");
			SendText("MSRC", 5);
			Thread.Sleep(2000);
			SendText("MSRC", "?");
		}

		/// <summary>
		/// Select DVI 1 Input (AV)
		/// </summary>
		public void InputDvi1()
		{
			Debug.Console(DebugVerbose, this, "InputDvi1 executing...");
			SendText("MSRC", 3);
			Thread.Sleep(2000);
			SendText("MSRC", "?");
		}

		/// <summary>
		/// Select VGA 1
		/// </summary>
		public void InputVga1()
		{
			Debug.Console(DebugVerbose, this, "InputVga1 executing...");
			SendText("MSRC", 0);
			Thread.Sleep(2000);
			SendText("MSRC", "?");
		}

		/// <summary>
		/// Toggles the display input
		/// </summary>
		public void InputToggle()
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Poll input
		/// </summary>
		public void InputGet()
		{
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
				Debug.Console(DebugNotice, this, "UpdateInputFb: _currentInputPort-'{0}' == newInput-'{1}'", _currentInputPort.Key, newInput.Key);
				return;
			}

			Debug.Console(DebugNotice, this, "UpdateInputFb: newInput key-'{0}', connectionType-'{1}', feedbackMatchObject-'{2}'",
				newInput.Key, newInput.ConnectionType, newInput.FeedbackMatchObject);

			_currentInputPort = newInput;
			CurrentInputFeedback.FireUpdate();

			Debug.Console(DebugNotice, this, "UpdateInputFb: _currentInputPort.key-'{0}'", _currentInputPort.Key);

			switch (_currentInputPort.Key)
			{
				case RoutingPortNames.HdmiIn1:
					CurrentInputNumber = 1;
					break;
				case RoutingPortNames.HdmiIn2:
					CurrentInputNumber = 2;
					break;
				case RoutingPortNames.DviIn1:
					CurrentInputNumber = 3;
					break;
				case RoutingPortNames.VgaIn1:
					CurrentInputNumber = 4;
					break;
				case RoutingPortNames.HdmiIn5:
					CurrentInputNumber = 5;
					break;
				case RoutingPortNames.HdmiIn4:
					CurrentInputNumber = 6;
					break;
			}
		}

		/// <summary>
		/// Updates Digital Route Feedback for Simpl EISC
		/// </summary>
		private void UpdateInputBooleanFeedback()
		{
			foreach (var item in InputFeedback)
			{
				item.FireUpdate();
			}
		}

		#endregion

		#region Power

		private bool _isCoolingDown;
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

				if (_isWarmingUp)
				{
					WarmupTimer = new CTimer(t =>
					{
						_isWarmingUp = false;
						IsWarmingUpFeedback.FireUpdate();
					}, WarmupTime);
				}
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

				if (_isCoolingDown)
				{
					CooldownTimer = new CTimer(t =>
					{
						_isCoolingDown = false;
						IsCoolingDownFeedback.FireUpdate();
					}, CooldownTime);
				}
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
			if (IsWarmingUp || IsCoolingDown) return;

			if (!PowerIsOn) IsWarmingUp = true;

			SendText("POWR", 1);		// power on, alternate cmds: "SBPM", "DPON"
		}

		/// <summary>
		/// Set Power Off for Device
		/// </summary>
		public override void PowerOff()
		{
			if (IsWarmingUp || IsCoolingDown) return;

			if (PowerIsOn) IsCoolingDown = true;

			SendText("POWR", 0);		// power off, alternate cmds: "SBPM", "DPON" 
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
			PowerGet();

			if (!PowerIsOn) return;

			CrestronEnvironment.Sleep(2000);
			InputGet();

			if (!HasLamps) return;

			CrestronEnvironment.Sleep(2000);
			LampGet();
		}


		/// <summary>
		/// Lamp hours feedback
		/// </summary>
		public IntFeedback LampHoursFeedback { get; set; }

		private int _lampHours;

		/// <summary>
		/// Lamp hours property
		/// </summary>
		public int LampHours
		{
			get { return _lampHours; }
			set
			{
				_lampHours = value;
				LampHoursFeedback.FireUpdate();
			}
		}

		/// <summary>
		/// Polls for lamp hours/laser runtime
		/// </summary>
		public void LampGet()
		{
			SendText("LSHS", "?");
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