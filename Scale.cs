using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace CrabWeighing
{
    sealed class WeightSample
    {
        public readonly DateTimeOffset Time = DateTimeOffset.Now;
        public readonly long Tick;
        public readonly double Value;
        public readonly string Unit;
        public readonly bool Stable;
        public readonly bool Valid;
        public readonly int Digits;

        public WeightSample(double value, string unit, bool stable, bool valid, int digits, long? tick = null)
        {
            Tick = tick ?? Stopwatch.GetTimestamp();
            Value = value;
            Unit = unit;
            Stable = stable;
            Valid = valid && !double.IsNaN(value) && !double.IsInfinity(value) && !string.IsNullOrWhiteSpace(unit);
            Digits = Math.Max(0, Math.Min(9, digits));
        }

        public string Number { get { return Value.ToString("F" + Digits, CultureInfo.InvariantCulture); } }
    }

    enum ScaleProtocol { Active, Passive, OpBox }

    sealed class Os2Scale
    {
        const ushort SerialWithUsbDiscovery = 1;
        static readonly string[] Units = { "g", "kg", "t", "港分", "港钱", "港两", "港斤", "港担",
            "台分", "台钱", "台两", "台斤", "台担", "oz", "lb", "klb" };

        // The driver holds native callback pointers for the lifetime of the connection.
        readonly OpCallback opCallback;
        readonly IsCallback isCallback;
        WeightSample latest;
        volatile bool accepting;
        volatile ScaleProtocol protocol;
        public bool IsOpen { get; private set; }

        public Os2Scale()
        {
            opCallback = ReceiveOp;
            isCallback = ReceiveIs;
        }

        public WeightSample Latest { get { return Volatile.Read(ref latest); } }

        public void Open(string port, ScaleProtocol selectedProtocol)
        {
            protocol = selectedProtocol;
            Interlocked.Exchange(ref latest, null);
            __SetCallBackFunc("SENSORINFO", Marshal.GetFunctionPointerForDelegate(opCallback));
            __SetCallBackFunc("IS1INFO", Marshal.GetFunctionPointerForDelegate(isCallback));
            accepting = true;
            try
            {
                // Restore SDK discovery before each open, including after a USB session.
                __SetCommMode(SerialWithUsbDiscovery);
                if (!__Open(port, port.Length == 0 ? 0 : 9600))
                    throw new InvalidOperationException("秤打开失败。请检查设备、驱动、COM 口和占用情况。");
                IsOpen = true;
            }
            finally
            {
                accepting = IsOpen;
            }
        }

        public void Close()
        {
            accepting = false;
            Interlocked.Exchange(ref latest, null);
            if (!IsOpen) return;
            if (!__Close()) throw new InvalidOperationException("称重 SDK 未确认关闭，请重试。");
            IsOpen = false;
        }

        public void Poll()
        {
            if (!accepting) return;
            if (protocol == ScaleProtocol.OpBox)
            {
                __Weight("1234");
            }
            else if (protocol == ScaleProtocol.Passive)
            {
                string response = Marshal.PtrToStringAnsi(__udeWeight());
                if (string.IsNullOrWhiteSpace(response)) return;
                string[] fields = response.Split(',');
                if (fields.Length >= 4) ReceiveIs(fields[0], fields[1], fields[2], fields[3], 0);
            }
        }

        void ReceiveOp(double net, double tare, double gross, int mode, int digits, int unit,
            int oldAd, int filterAd, int zeroAd, int weightAd, int flags)
        {
            if (!accepting || protocol != ScaleProtocol.OpBox) return;
            string symbol = unit >= 0 && unit < Units.Length ? Units[unit] : "";
            var sample = new WeightSample(net, symbol, (flags & 1) != 0, (flags & 6) == 0, digits);
            Interlocked.Exchange(ref latest, sample);
        }

        void ReceiveIs(string status, string sign, string weight, string unit, int flags)
        {
            if (!accepting || protocol == ScaleProtocol.OpBox) return;
            status = (status ?? "").Trim();
            string number = (sign ?? "").Trim() + (weight ?? "").Trim();
            double value;
            bool parsed = double.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value);
            int point = number.IndexOf('.');
            var sample = new WeightSample(value, (unit ?? "").Trim(), status == "S",
                parsed && (status == "S" || status == "U"), point < 0 ? 0 : number.Length - point - 1);
            Interlocked.Exchange(ref latest, sample);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void OpCallback(double net, double tare, double gross, int mode, int digits, int unit,
            int oldAd, int filterAd, int zeroAd, int weightAd, int flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        delegate void IsCallback(string status, string sign, string weight, string unit, int flags);

        [DllImport("SensorDll.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern bool __Open(string port, int baud);

        [DllImport("SensorDll.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        static extern bool __Close();

        [DllImport("SensorDll.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        static extern void __SetCommMode(ushort mode);

        [DllImport("SensorDll.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern bool __Weight(string id);

        [DllImport("SensorDll.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        static extern IntPtr __udeWeight();

        [DllImport("SensorDll.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern void __SetCallBackFunc(string name, IntPtr callback);
    }
}
