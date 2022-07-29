using System;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PPAGUI.Hardware
{
    public class Rs232Scanner
    {
        private readonly SerialPort _serialPort;
        private string _temporaryRead;
        private bool _readStart;
        private bool _dataReadValidInvoked;

        public delegate Task DataReadValid(object sender);

        public event DataReadValid OnDataReadValid;
        public bool DataIsValid { get; protected set; }
        public string DataValue { get; protected set; }

        public Rs232Scanner()
        {

        }
        public Rs232Scanner(SerialPort serialPort)
        {
            _serialPort = serialPort;
            _serialPort.DataReceived += OnDataReceived;
            var timer = new Timer();
            timer.Interval = 1;
            timer.Start();
            timer.Tick += TimerTick;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            if (DataIsValid && !_dataReadValidInvoked)
            {

                try
                {
                    _dataReadValidInvoked = true;
                    OnDataReadValid?.Invoke(this);
                }
                catch
                {
                    //
                }
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!_readStart)
            {
                _serialPort?.ReadExisting();
                return;
            }
            _temporaryRead += _serialPort?.ReadExisting();
            if (_temporaryRead.Contains('\r') || _temporaryRead.Contains('\n'))
            {
                _temporaryRead = _temporaryRead.Trim();
                DataValue = _temporaryRead;
                DataIsValid = true;
                _readStart = false;
                _temporaryRead = "";
            }
        }

        public void StartRead()
        {
            if (_serialPort != null)
            {
                if (!_serialPort.IsOpen )
                {
                    try
                    {
                        _serialPort.Open();
                    }
                    catch
                    {
                        //
                    }
                }
            }
            if (!_readStart)
            {
                _temporaryRead = "";
                _readStart = true;
                DataIsValid = false;
                _dataReadValidInvoked = false;
            }
        }
        public void StopRead()
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    try
                    {
                        _serialPort.Close();
                    }
                    catch
                    {
                        //
                    }
                }
            }
            _readStart = false;
            _dataReadValidInvoked = false;
        }
    }
}
