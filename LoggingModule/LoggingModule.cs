﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO; 
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyslogLogging
{
    /// <summary>
    /// Syslog, console, and file logging module.
    /// </summary>
    public class LoggingModule : IDisposable
    { 
        #region Public-Members

        /// <summary>
        /// Server IP address.
        /// </summary>
        public string ServerIp
        {
            get
            {
                return _ServerIp;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(ServerIp));

                IPAddress addr = null;
                if (!IPAddress.TryParse(value, out addr)) throw new ArgumentException("Use an IP address instead of a hostname.");

                _ServerIp = value;
            }
        }

        /// <summary>
        /// UDP port on which the syslog server is listening.
        /// </summary>
        public int ServerPort
        {
            get
            {
                return _ServerPort;
            }
            set
            {
                if (value < 0 || value > 65535) throw new ArgumentException("Port must be in the range 0-65535.");
                _ServerPort = value;
            }
        }

        /// <summary>
        /// Enable or disable console logging.
        /// </summary>
        public bool ConsoleEnable
        {
            get
            {
                return _ConsoleEnable;
            }
            set
            {
                if (value && !ConsoleExists())
                {
                    _ConsoleEnable = false;
                }
                else
                {
                    _ConsoleEnable = value;
                }
            }
        }

        /// <summary>
        /// Minimum severity required to send a message.
        /// </summary>
        public Severity MinimumSeverity = Severity.Debug;

        /// <summary>
        /// Enable or disable async logging.
        /// </summary>
        public bool AsyncLogging = false;

        /// <summary>
        /// Include the UTC timestamp in the message.
        /// </summary>
        public bool IncludeUtcTimestamp = true;

        /// <summary>
        /// Include the severity in the message.
        /// </summary>s
        public bool IncludeSeverity = true;

        /// <summary>
        /// Include the local hostname in the message.
        /// </summary>
        public bool IncludeHostname = false;

        /// <summary>
        /// Include the local thread ID in the message.
        /// </summary>
        public bool IncludeThreadId = false;

        /// <summary>
        /// Indent outgoing messages based on stack depth.
        /// </summary>
        public bool IndentByStackSize = false;

        /// <summary>
        /// Enable or disable use of color for console messages.
        /// </summary>
        public bool EnableColors = true;

        /// <summary>
        /// Colors to use for console messages based on message severity.
        /// </summary>
        public ColorSchema Colors = new ColorSchema();

        /// <summary>
        /// Enable or disable logging to a file.
        /// Disabled: file logging will not be used.
        /// SingleLogFile: all messages will be appended to a single file.
        /// FileWithDate: all messages will be appended to a file, where the name of the file is the supplied filename followed by '.yyyyMMdd'.
        /// </summary>
        public FileLoggingMode FileLogging = FileLoggingMode.Disabled;

        /// <summary>
        /// The file to which log messages should be appended.
        /// </summary>
        public string LogFilename = null;

        /// <summary>
        /// The severity level to use when logging exceptions through the .Exception() method.  
        /// </summary>
        public Severity ExceptionSeverity = Severity.Alert;

        /// <summary>
        /// Maximum message length.  Must be greater than or equal to 32.  Default is 1024.
        /// </summary>
        public int MaxMessageLength
        {
            get
            {
                return _MaxMessageLength;
            }
            set
            {
                if (value < 32) throw new ArgumentException("Maximum message length must be at least 32.");
                _MaxMessageLength = value;
            }
        }

        #endregion

        #region Private-Members

        private bool _Disposed = false;
        private string _ServerIp = "127.0.0.1";
        private int _ServerPort = 514; 
        private UdpClient _UDP = null;
        private string _Hostname = null;
        private object _SendLock = new object();
        private int _BaseDepth = 0;
        private bool _ConsoleEnable = true;
        private int _MaxMessageLength = 1024;
        private readonly object _FileLock = new object();

        #endregion

        #region Constructor

        /// <summary>
        /// Instantiate the object using localhost syslog (UDP port 514).
        /// </summary>
        public LoggingModule()
        {
            ServerIp = "127.0.0.1";
            ServerPort = 514;

            _UDP = new UdpClient(ServerIp, ServerPort);
            _Hostname = Dns.GetHostName();

            StackTrace st = new StackTrace();
            _BaseDepth = st.FrameCount - 1;
        }

        /// <summary>
        /// Instantiate the object using the specified syslog server IP address and UDP port.
        /// </summary>
        /// <param name="serverIp">Server IP address.</param>
        /// <param name="serverPort">Server port number.</param>
        public LoggingModule(
            string serverIp,
            int serverPort)
        {
            ServerIp = serverIp;
            ServerPort = serverPort;

            _UDP = new UdpClient(ServerIp, ServerPort);
            _Hostname = Dns.GetHostName();

            StackTrace st = new StackTrace();
            _BaseDepth = st.FrameCount - 1; 
        }

        /// <summary>
        /// Instantiate the object to enable either file logging or console logging.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <param name="enableConsole">Enable or disable console logging.</param>
        public LoggingModule(
            string filename,
            bool enableConsole)
        {
            if (String.IsNullOrEmpty(filename) && !enableConsole) throw new ArgumentException("Either a filename must be specified or console logging must be enabled.");

            ServerIp = "127.0.0.1";
            ServerPort = 514;
            _UDP = null;
            _Hostname = null;

            StackTrace st = new StackTrace();
            _BaseDepth = st.FrameCount - 1;

            if (!String.IsNullOrEmpty(filename))
            {
                LogFilename = filename;
                FileLogging = FileLoggingMode.SingleLogFile;
            }

            ConsoleEnable = enableConsole;
        }

        /// <summary>
        /// Instantiate the object to enable either file logging or console logging.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <param name="fileLoggingMode">File logging mode.  If you specify 'FileWithDate', .yyyyMMdd will be appended to the specified filename.</param>
        /// <param name="enableConsole">Enable or disable console logging.</param>
        public LoggingModule(
            string filename,
            FileLoggingMode fileLoggingMode,
            bool enableConsole)
        {
            if (String.IsNullOrEmpty(filename)) throw new ArgumentNullException(nameof(filename));

            ServerIp = "127.0.0.1";
            ServerPort = 514;
            _UDP = null;
            _Hostname = null;

            StackTrace st = new StackTrace();
            _BaseDepth = st.FrameCount - 1;

            LogFilename = filename;
            FileLogging = fileLoggingMode;

            ConsoleEnable = enableConsole;
        }

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        /// <param name="serverIp">Server IP address.</param>
        /// <param name="serverPort">Server port number.</param>
        /// <param name="consoleEnable">Enable or disable console logging.</param>
        /// <param name="minimumSeverity">Minimum severity required to send a message.</param>
        /// <param name="asyncLogging">Enable or disable async logging.</param>
        /// <param name="includeUtcTimestamp">Include the UTC timestamp in the message.</param>
        /// <param name="includeSeverity">Include the severity in the message.</param>
        /// <param name="includeHostname">Include the local hostname in the message.</param>
        /// <param name="includeThreadId">Include the local thread ID in the message.</param>
        /// <param name="indentByStackSize">Indent outgoing messages based on stack depth.</param>
        public LoggingModule(
            string serverIp,
            int serverPort,
            bool consoleEnable,
            Severity minimumSeverity,
            bool asyncLogging,
            bool includeUtcTimestamp,
            bool includeSeverity,
            bool includeHostname,
            bool includeThreadId,
            bool indentByStackSize)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 0 && serverPort > 65535) throw new ArgumentException("Server port must in the range 0-65535.");

            ServerIp = serverIp;
            ServerPort = serverPort;
            ConsoleEnable = consoleEnable;
            MinimumSeverity = minimumSeverity;
            AsyncLogging = asyncLogging;
            IncludeUtcTimestamp = includeUtcTimestamp;
            IncludeSeverity = includeSeverity;
            IncludeHostname = includeHostname;
            IncludeThreadId = includeThreadId;
            IndentByStackSize = indentByStackSize;
               
            _UDP = new UdpClient(ServerIp, ServerPort); 
            _Hostname = Dns.GetHostName();

            StackTrace st = new StackTrace();
            _BaseDepth = st.FrameCount - 1;
        }

        #endregion

        #region Public-Methods
         
        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
          
        /// <summary>
        /// Send a log message using 'Debug' severity.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public virtual void Debug(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Log(Severity.Debug, msg);
        }

        /// <summary>
        /// Send a log message using 'Info' severity.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public virtual void Info(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Log(Severity.Info, msg); 
        }

        /// <summary>
        /// Send a log message using 'Warn' severity.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public virtual void Warn(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Log(Severity.Warn, msg);
        }

        /// <summary>
        /// Send a log message using 'Error' severity.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public virtual void Error(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Log(Severity.Error, msg);
        }

        /// <summary>
        /// Send a log message using 'Alert' severity.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public virtual void Alert(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Log(Severity.Alert, msg);
        }

        /// <summary>
        /// Send a log message using 'Critical' severity.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public virtual void Critical(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Log(Severity.Critical, msg);
        }

        /// <summary>
        /// Send a log message using 'Emergency' severity.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public virtual void Emergency(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Log(Severity.Emergency, msg);
        }
         
        /// <summary>
        /// Send log messages containing Exception details using 'Alert' severity.
        /// </summary>
        /// <param name="module">Module name (user-specified).</param>
        /// <param name="method">Method name (user-specified).</param>
        /// <param name="e">Exception.</param>
        public virtual void Exception(string module, string method, Exception e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            var st = new StackTrace(e, true);
            var frame = st.GetFrame(0);
            int fileLine = frame.GetFileLineNumber();
            string filename = frame.GetFileName();

            string message =
                Environment.NewLine +
                "--- Exception details ---" + Environment.NewLine +
                "  Module     : " + module + Environment.NewLine +
                "  Method     : " + method + Environment.NewLine +
                "  Type       : " + e.GetType().ToString() + Environment.NewLine;

            if (e.Data != null && e.Data.Count > 0)
            {
                message += "  Data       : " + Environment.NewLine;
                foreach (DictionaryEntry curr in e.Data)
                {
                    message += "  | " + curr.Key + ": " + curr.Value + Environment.NewLine;
                }
            }
            else
            {
                message += "  Data       : (none)" + Environment.NewLine;
            }

            message +=
                "  Inner      : ";

            if (e.InnerException == null) message += "(null)" + Environment.NewLine;
            else
            {
                message += e.InnerException.GetType().ToString() + Environment.NewLine;
                message +=
                    "    Message    : " + e.InnerException.Message + Environment.NewLine +
                    "    Source     : " + e.InnerException.Source + Environment.NewLine +
                    "    StackTrace : " + e.InnerException.StackTrace + Environment.NewLine +
                    "    ToString   : " + e.InnerException.ToString() + Environment.NewLine;

                if (e.InnerException.Data != null && e.InnerException.Data.Count > 0)
                {
                    message += "    Data       : " + Environment.NewLine;
                    foreach (DictionaryEntry curr in e.Data)
                    {
                        message += "    | " + curr.Key + ": " + curr.Value + Environment.NewLine;
                    }
                }
                else
                {
                    message += "    Data       : (none)" + Environment.NewLine;
                } 
            }

            message += 
                "  Message    : " + e.Message + Environment.NewLine +
                "  Source     : " + e.Source + Environment.NewLine +
                "  StackTrace : " + e.StackTrace + Environment.NewLine +
                "  Stack      : " + StackToString() + Environment.NewLine +
                "  Line       : " + fileLine + Environment.NewLine +
                "  File       : " + filename + Environment.NewLine +
                "  ToString   : " + e.ToString() + Environment.NewLine +
                "---";

            Log(ExceptionSeverity, message);
        }

        /// <summary>
        /// Send a log message using the specified severity.
        /// </summary>
        /// <param name="sev">Severity of the message.</param>
        /// <param name="msg">Message to send.</param>
        public virtual void Log(Severity sev, string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            if (sev < MinimumSeverity) return;

            string message = "";
            string currMsg = "";
            string remainder = "";

            if (msg.Length > _MaxMessageLength)
            {
                currMsg = msg.Substring(0, _MaxMessageLength);
                remainder = msg.Substring(_MaxMessageLength, (msg.Length - _MaxMessageLength));
            }
            else
            {
                currMsg = msg;
            }

            if (IncludeUtcTimestamp) message += DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") + " ";
            if (IncludeSeverity) message += FormattedSeverity(sev) + " ";
            if (IncludeHostname) message += _Hostname + " ";
            if (IncludeThreadId) message += "thr-" + Thread.CurrentThread.ManagedThreadId + " ";
            if (IndentByStackSize)
            {
                StackTrace st = new StackTrace();
                int CurrentDepth = st.FrameCount;
                if (CurrentDepth > _BaseDepth)
                {
                    for (int i = 0; i < (CurrentDepth - _BaseDepth); i++)
                    {
                        message += " ";
                    }
                }
            }

            message += currMsg;

            if (ConsoleEnable)
            {
                if (!AsyncLogging) SendConsole(sev, message);
                else Task.Run(() => SendConsole(sev, message));
            }

            if (!String.IsNullOrEmpty(LogFilename) && FileLogging != FileLoggingMode.Disabled)
            {
                SendFile(sev, message);
            }

            if (_UDP != null)
            {
                if (!AsyncLogging) SendUdp(message);
                else Task.Run(() => SendUdp(message));
            }

            if (!String.IsNullOrEmpty(remainder))
            {
                Log(sev, remainder);
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose of the resource.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                if (_UDP != null)
                {
                    try
                    {
                        _UDP.Close();
                    }
                    catch (Exception)
                    {

                    }
                }
            }

            _Disposed = true;
        }

        private bool ConsoleExists()
        {
            try
            {
                bool test1 = Environment.UserInteractive;
                bool test2 = Console.WindowHeight > 0;
                return test1 && test2;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SendConsole(Severity sev, string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            if (!_ConsoleEnable) return;

            if (EnableColors)
            {
                ConsoleColor prevForeground = Console.ForegroundColor;
                ConsoleColor prevBackground = Console.BackgroundColor;

                if (Colors != null)
                {
                    switch (sev)
                    {
                        case Severity.Debug:
                            Console.ForegroundColor = Colors.Debug.Foreground;
                            Console.BackgroundColor = Colors.Debug.Background;
                            break;
                        case Severity.Info:
                            Console.ForegroundColor = Colors.Info.Foreground;
                            Console.BackgroundColor = Colors.Info.Background;
                            break;
                        case Severity.Warn:
                            Console.ForegroundColor = Colors.Warn.Foreground;
                            Console.BackgroundColor = Colors.Warn.Background;
                            break;
                        case Severity.Error:
                            Console.ForegroundColor = Colors.Error.Foreground;
                            Console.BackgroundColor = Colors.Error.Background;
                            break;
                        case Severity.Alert:
                            Console.ForegroundColor = Colors.Alert.Foreground;
                            Console.BackgroundColor = Colors.Alert.Background;
                            break;
                        case Severity.Critical:
                            Console.ForegroundColor = Colors.Critical.Foreground;
                            Console.BackgroundColor = Colors.Critical.Background;
                            break;
                        case Severity.Emergency:
                            Console.ForegroundColor = Colors.Emergency.Foreground;
                            Console.BackgroundColor = Colors.Emergency.Background;
                            break;
                    }
                }

                Console.WriteLine(msg);
                Console.ForegroundColor = prevForeground;
                Console.BackgroundColor = prevBackground;
            }
            else
            {
                Console.WriteLine(msg);
            }
        }

        private void SendFile(Severity sev, string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;

            switch (FileLogging)
            {
                case FileLoggingMode.Disabled:
                    return;

                case FileLoggingMode.SingleLogFile:
                    lock (_FileLock)
                    {
                        File.AppendAllText(LogFilename, msg + Environment.NewLine);
                    }
                    return;

                case FileLoggingMode.FileWithDate:
                    string filename = LogFilename + "." + DateTime.Now.ToString("yyyyMMdd");
                    lock (_FileLock)
                    {
                        File.AppendAllText(filename, msg + Environment.NewLine);
                    }
                    return;
            }
        }

        private void SendUdp(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            
            lock (_SendLock)
            {
                if (_UDP != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(msg);

                    try
                    {
                        _UDP.Send(data, data.Length);
                    }
                    catch (Exception)
                    { 
                    }
                }
            } 
        }

        private string StackToString()
        {
            string ret = "";

            StackTrace t = new StackTrace();
            for (int i = 2; i < t.FrameCount; i++)
            {
                if (i == 2)
                {
                    ret += t.GetFrame(i).GetMethod().Name;
                }
                else
                {
                    ret += " <= " + t.GetFrame(i).GetMethod().Name;
                }
            }

            return ret;
        }
         
        private string FormattedSeverity(Severity sev)
        {
            switch (sev)
            {
                case Severity.Debug:
                    return "[Debug    ]";
                case Severity.Info:
                    return "[Info     ]";
                case Severity.Warn:
                    return "[Warn     ]";
                case Severity.Error:
                    return "[Error    ]";
                case Severity.Alert:
                    return "[Alert    ]";
                case Severity.Critical:
                    return "[Critical ]";
                case Severity.Emergency:
                    return "[Emergency]";
                default:
                    throw new ArgumentException("Unknown severity: " + sev.ToString() + ".");
            }
        }
          
        #endregion
    }
}
