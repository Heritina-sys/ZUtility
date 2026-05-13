using zkemkeeper;
using ZKUtility.Models;

namespace ZKUtility.Services
{
    public class DeviceService
    {
        private CZKEM _zk = new CZKEM();

        public bool Connect(string ip, int port)
        {
            _zk.SetCommPassword(0);
            return _zk.Connect_Net(ip, port);
        }

        public void Disconnect()
        {
            _zk.Disconnect();
        }

        public List<DeviceLog> GetLogs(int machineNumber)
        {
            var logs = new List<DeviceLog>();

            if (!_zk.ReadAllGLogData(machineNumber))
                return logs;

            string enrollNumber;
            int verifyMode, inOutMode, year, month, day, hour, minute, second;
            int workCode = 0;

            while (_zk.SSR_GetGeneralLogData(
                machineNumber,
                out enrollNumber,
                out verifyMode,
                out inOutMode,
                out year,
                out month,
                out day,
                out hour,
                out minute,
                out second,
                ref workCode))
            {
                if (!uint.TryParse(enrollNumber, out uint enroll))
                    continue;

                logs.Add(new DeviceLog
                {
                    EnrollNumber = enroll,
                    VerifyMode = verifyMode,
                    LogTime = new DateTime(year, month, day, hour, minute, second)
                });
            }

            return logs;
        }
    }
}