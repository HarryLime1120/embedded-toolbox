﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StatusConsoleApi;
using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StatusConsole {
    public class SerialPortConnector : ITtyService {
        private ILogger Log;
        private IConfigurationSection Config;
        private ISerialProtocol protocol;

        private SerialPort Port;
        private String OnConnect;
        private String CmdTerminator;

        public void Initialize(IConfigurationSection cs, IConfiguration rootConfig, ILogger logger) {
            Config = cs;
            OnConnect = cs.GetValue<String>("OnConnect", "");
            CmdTerminator = Config?.GetValue<String>("NewLine") ?? "\r";
            Log = logger;

            // Here we Instanciate the configured protocol for this Serial Connector. If there is an protocol config section declared we pass it on to the 
            // ISerialProtocol constructor.
            string typeName = Config?.GetValue<String>("ProtClass") ?? "StatusConsole.DefaultCli";

            try {
                String configName = Config?.GetValue<String>("ProtConfig") ?? "dummyCfg";
                IConfigurationSection pluginConfig = rootConfig.GetSection(configName);
                protocol = PluginSystem.LoadPlugin<ISerialProtocol>(typeName, pluginConfig);
            } catch (Exception) {
                throw new ApplicationException("Protocol Class (" + typeName + ") not found for '" + cs.Path + "' ");
            }
        }

        public void SetScreen(IOutputWrapper scr) {
            protocol.SetScreen(scr, Log, this, CmdTerminator);
        }

        public void ProcessCommand(String cl) {
            protocol.ProcessUserInput(cl);
            //Port.Write(Encoding.ASCII.GetBytes(s + Port.NewLine), 0, (s + Port.NewLine).Length);
        }

        public void SendUart(byte[] bytes, int len) {
            try {
                Port?.Write(bytes, 0, len);
                Log?.LogDebug(new EventId(1, "Tx"),"{@line}", bytes);
            } catch (Exception ex) {
                Log?.LogError(new EventId(1, "Tx"), "Send Error: " + ex.Message);
            }
        }

        public string GetViewName() {
            return Config.GetValue<String>("Screen", "");
        }

        public string GetInterfaceName() {
            return Config.Key;
        }

        public bool IsConnected() {
            return Port.IsOpen;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            try {
                Port = new SerialPort();
                Port.PortName = Config?.GetValue<String>("ComName") ?? "COM1";
                Port.BaudRate = Config?.GetValue<int?>("Baud") ?? 9600;
                Port.Parity = Parity.None;
                Port.DataBits = 8;
                Port.StopBits = StopBits.One;
                Port.Handshake = Handshake.None;
                Port.Encoding = Encoding.ASCII;
                Port.Open();
                Port.DtrEnable = true;
                Port.NewLine = Config?.GetValue<String>("NewLine") ?? "\r";

                Log?.LogInformation(new EventId(0), "Uart Uart {@portname} connected. Using {@newline} as newline char.", Port.PortName, "0x" + Convert.ToByte(Port.NewLine[0]).ToString("X2"));

                Port.DataReceived += Port_DataReceived;
                Port.ErrorReceived += Port_ErrorReceived;
                Port.PinChanged += Port_PinChanged;
                Port.Disposed += Port_Disposed;

                //Log?.LogInformation(new EventId(0), "Sending OnConnect command {@cmd}", OnConnect);
                SendUart(Encoding.ASCII.GetBytes((OnConnect ?? "") + Port.NewLine), Encoding.ASCII.GetBytes(OnConnect ?? "").Length + 1);
            } catch (Exception ex) {
                //Continue = false;
                Log?.LogError(new EventId(0), ex, "Error starting '" + Config?.GetValue<String>("ComName") ?? "<null>->COM1" + "' !");
            }
            return Task.CompletedTask;
        }

        private void Port_Disposed(object sender, EventArgs e) {
            Log?.LogInformation(new EventId(4, "Rx"), "Serial Port disoposed ");
        }

        private void Port_PinChanged(object sender, SerialPinChangedEventArgs e) {
            Log?.LogInformation(new EventId(3, "Rx"), "Pin Changed: " + e.EventType);
        }

        private void Port_ErrorReceived(object sender, SerialErrorReceivedEventArgs e) {
            Log?.LogError(new EventId(2, "Rx"), "Serial Error: " + e.EventType);
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e) {
            if (e.EventType == SerialData.Chars) {
                try {
                    SerialPort sp = (SerialPort)sender;
                    while (sp.BytesToRead > 0) {
                        var b = sp.ReadByte();
                        if (b >= 0) {
                            Log?.LogTrace("Rx: {@mycharHex} '{@mychar}'", "0x" + b.ToString("X2"), (b == '\n') ? ' ' : b);
                            protocol.ProcessByte((byte)b);
                        }
                    };
                } catch (Exception ex) {
                    Log?.LogError("Rx error using SerialPort in event handler. " + this.Config.Key, ex);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            if (Port != null) {
                Port.DtrEnable = false;
                Port.Close();
            }
            Log?.LogInformation(new EventId(0), "Uart {@portname} closed.", Port?.PortName??"<null>");
            return Task.CompletedTask;
        }
    }
}
