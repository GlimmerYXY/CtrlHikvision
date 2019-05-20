using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Data;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Configuration;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Security.Principal;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace CtrlHikvision
{
    // Defines the data protocol for reading and writing strings on our stream
    public class StreamString
    {
        private Stream ioStream;
        private UnicodeEncoding streamEncoding;

        public StreamString(Stream ioStream)
        {
            this.ioStream = ioStream;
            streamEncoding = new UnicodeEncoding();
        }

        public string ReadString()
        {
            int len;
            len = ioStream.ReadByte() * 256;
            len += ioStream.ReadByte();
            byte[] inBuffer = new byte[len];
            ioStream.Read(inBuffer, 0, len);

            return streamEncoding.GetString(inBuffer);
        }

        public int WriteString(string outString)
        {
            byte[] outBuffer = streamEncoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > UInt16.MaxValue)
            {
                len = (int)UInt16.MaxValue;
            }
            ioStream.WriteByte((byte)(len / 256));
            ioStream.WriteByte((byte)(len & 255));
            ioStream.Write(outBuffer, 0, len);
            ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }

    class Program
    {
        #region 全局变量

        private string DVRIPAddress;    //管理机：99-admin123，门口机：90-Hik12345
        private Int16 DVRPortNumber;
        private string DVRUserName;
        private string DVRPassword;

        private uint iLastErr = 0;
        private Int32 m_lUserID = -1;
        private Int32 m_lAlarmHandle = -1;

        private byte preDoorSta;
        private byte curDoorSta = 2;
        private byte m_DoorStatus;
        private object fileObj = new object();
        
        private CHCNetSDK.MSGCallBack_V31 m_falarmData_V31 = null;
        private CHCNetSDK.NET_DVR_DEVICEINFO_V30 DeviceInfo = new CHCNetSDK.NET_DVR_DEVICEINFO_V30();

        private NamedPipeClientStream pipeClient;
        private StreamString ss;

        #endregion

        static void Main(string[] args)
        {
            Program pro = new Program();

            pro.AccessAppSettings();
            //string str2 = Environment.CurrentDirectory;//获取和设置当前目录（即该进程从中启动的目录）的完全限定路径。
            //string str1 = Process.GetCurrentProcess().MainModule.FileName;//可获得当前执行的exe的文件名。  
            //string str3 = Directory.GetCurrentDirectory();//获取应用程序的当前工作目录。
            //string str4 = AppDomain.CurrentDomain.BaseDirectory;//获取基目录，它由程序集冲突解决程序用来探测程序集。
            //string str7 = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;//获取或设置包含该应用程序的目录的名称。

            // 注册
            bool m_bInitSDK = CHCNetSDK.NET_DVR_Init();
            if (m_bInitSDK == false)
            {
                Console.WriteLine("NET_DVR_Init error!");
                //return;
            }
            else
            {
                //保存SDK日志 To save the SDK log
                CHCNetSDK.NET_DVR_SetLogToFile(3, "C:\\SdkLog\\", true);

                // 登录设备
                pro.m_lUserID = CHCNetSDK.NET_DVR_Login_V30(pro.DVRIPAddress, pro.DVRPortNumber, pro.DVRUserName, pro.DVRPassword, ref pro.DeviceInfo);
                if (pro.m_lUserID < 0)
                {
                    pro.iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                    Console.WriteLine("NET_DVR_Login_V30 failed, error code= " + pro.iLastErr);
                    //return;
                }
                else
                {
                    Console.WriteLine("NET_DVR_Login_V30 succeeded!");
                }

                //设置报警回调函数
                if (pro.m_falarmData_V31 == null)
                {
                    pro.m_falarmData_V31 = new CHCNetSDK.MSGCallBack_V31(pro.MsgCallback_V31);
                }
                bool res = CHCNetSDK.NET_DVR_SetDVRMessageCallBack_V31(pro.m_falarmData_V31, IntPtr.Zero);
                if (!res)
                {
                    pro.iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                    Console.WriteLine("NET_DVR_SetDVRMessageCallBack_V31 failed, error code= " + pro.iLastErr);
                    //return;
                }
                else
                {
                    Console.WriteLine("NET_DVR_SetDVRMessageCallBack_V31 succeeded!");
                }

                ////建立进程间管道
                //pro.pipeClient = new NamedPipeClientStream(".", "Hik&Cortex",
                //PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
                //Console.WriteLine("连接到 Hik&Cortex管道 ...\n");
                //pro.pipeClient.Connect();
                //pro.ss = new StreamString(pro.pipeClient);

                //启用布防
                pro.m_SetAlarm();

                //监听管道
                while (true)
                {
                    //string cmd = pro.ss.ReadString();
                    //if(cmd == "open the door")
                    //{
                    //    pro.OpenDoor(); 
                    //}
                }
            }
        }

        private void m_SetAlarm()
        {
            CHCNetSDK.NET_DVR_SETUPALARM_PARAM struAlarmParam = new CHCNetSDK.NET_DVR_SETUPALARM_PARAM();
            struAlarmParam.dwSize = (uint)Marshal.SizeOf(struAlarmParam);
            struAlarmParam.byLevel = 1; //0- 一级布防,1- 二级布防
            struAlarmParam.byAlarmInfoType = 1;//智能交通设备有效，新报警信息类型
            struAlarmParam.byFaceAlarmDetection = 1;//1-人脸侦测

            m_lAlarmHandle = CHCNetSDK.NET_DVR_SetupAlarmChan_V41(m_lUserID, ref struAlarmParam);
            if (m_lAlarmHandle < 0)
            {
                //布防失败，输出错误号
                iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                Console.WriteLine("NET_DVR_SetupAlarmChan_V41 failed, error code= " + iLastErr);
            }
            else
            {
                Console.WriteLine("NET_DVR_SetupAlarmChan_V41 succeeded!");
            }
        }

        private void m_CloseAlarm()
        {
            if (m_lAlarmHandle >= 0)
            {
                if (!CHCNetSDK.NET_DVR_CloseAlarmChan_V30(m_lAlarmHandle))
                {
                    //撤防失败，输出错误号
                    iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                    Console.WriteLine("NET_DVR_CloseAlarmChan_V30 failed, error code= " + iLastErr);
                }
                else
                {
                    //未布防
                    Console.WriteLine("NET_DVR_CloseAlarmChan_V30 succeeded!");
                    m_lAlarmHandle = -1;
                }
            }
            else
            {
                //未布防
                Console.WriteLine("Haven't set alarm");
            }
        }

        private void m_Exit()
        {
            //撤防
            m_CloseAlarm();

            //注销登录
            CHCNetSDK.NET_DVR_Logout(m_lUserID);

            //释放SDK资源，在程序结束之前调用
            CHCNetSDK.NET_DVR_Cleanup();
        }

        private bool MsgCallback_V31(int lCommand, ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            //通过lCommand来判断接收到的报警信息类型，不同的lCommand对应不同的pAlarmInfo内容
            AlarmMessageHandle(lCommand, ref pAlarmer, pAlarmInfo, dwBufLen, pUser);

            return true; //回调函数需要有返回，表示正常接收到数据
        }

        private void MsgCallback(int lCommand, ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            //通过lCommand来判断接收到的报警信息类型，不同的lCommand对应不同的pAlarmInfo内容
            AlarmMessageHandle(lCommand, ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
        }

        private void AlarmMessageHandle(int lCommand, ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            //Console.WriteLine(DateTime.Now.ToString() + "\t报警信息类型：" + lCommand);
            //通过lCommand来判断接收到的报警信息类型，不同的lCommand对应不同的pAlarmInfo内容
            switch (lCommand)
            {
                case CHCNetSDK.COMM_UPLOAD_VIDEO_INTERCOM_EVENT://可视对讲事件记录
                    ProcessCommAlarm_VideoInterComAlarm(ref pAlarmer, pAlarmInfo, dwBufLen, pUser);
                    break;
                default:
                    //{
                    //    //报警设备IP地址
                    //    string strIP = System.Text.Encoding.UTF8.GetString(pAlarmer.sDeviceIP).TrimEnd('\0');
                    //    //报警信息类型
                    //    string stringAlarm = "报警上传，信息类型：" + lCommand;

                    //    Console.WriteLine(DateTime.Now.ToString() + "\t" + strIP + "\t" + stringAlarm);
                    //}
                    break;
            }
        }

        //可视事件
        private void ProcessCommAlarm_VideoInterComAlarm(ref CHCNetSDK.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT struVideoInterComEvent = new CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT();
            uint dwSize = (uint)Marshal.SizeOf(struVideoInterComEvent);
            struVideoInterComEvent = (CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT)Marshal.PtrToStructure(pAlarmInfo, typeof(CHCNetSDK.NET_DVR_VIDEO_INTERCOM_EVENT));
            
            //获取门状态
            IntPtr ptrDoorStatusInfo = Marshal.AllocHGlobal((Int32)dwSize);
            Marshal.StructureToPtr(struVideoInterComEvent.uEventInfo, ptrDoorStatusInfo, false);
            CHCNetSDK.NET_DVR_DOOR_STATUS_INFO m_struDoorStatusInfo = new CHCNetSDK.NET_DVR_DOOR_STATUS_INFO();
            m_struDoorStatusInfo = (CHCNetSDK.NET_DVR_DOOR_STATUS_INFO)Marshal.PtrToStructure(ptrDoorStatusInfo, typeof(CHCNetSDK.NET_DVR_DOOR_STATUS_INFO));

            string stringAlarm = DateTime.Now.ToString("HH:mm:ss   ") + "可视对讲事件，byEventType ：" + struVideoInterComEvent.byEventType
                + "，门锁状态：" + m_struDoorStatusInfo.byDoorStatus;
            Console.WriteLine(stringAlarm);

            preDoorSta = curDoorSta;
            curDoorSta = m_struDoorStatusInfo.byDoorStatus;
            WriteLog(curDoorSta.ToString());
            
            //if (preDoorSta == 0 && curDoorSta == 1)
            //{
            //    //开门
            //    ss.WriteString("1");
            //}
            //else if(preDoorSta == 1 && curDoorSta == 0)
            //{
            //    //关门
            //    ss.WriteString("0");
            //}
        }

        //远程开门
        private void OpenDoor()
        {
            CHCNetSDK.NET_DVR_CONTROL_GATEWAY struCtrlGate = new CHCNetSDK.NET_DVR_CONTROL_GATEWAY();
            uint dwSize = (uint)Marshal.SizeOf(struCtrlGate);

            struCtrlGate.dwSize = dwSize;
            struCtrlGate.byCommand = 1;


            IntPtr ptrCtrlGate = Marshal.AllocHGlobal((int)dwSize);
            Marshal.StructureToPtr(struCtrlGate, ptrCtrlGate, false);

            if (!CHCNetSDK.NET_DVR_RemoteControl(m_lUserID, CHCNetSDK.NET_DVR_REMOTECONTROL_GATEWAY, ptrCtrlGate, dwSize))
            {   //失败
                iLastErr = CHCNetSDK.NET_DVR_GetLastError();
                Console.WriteLine("NET_DVR_REMOTECONTROL_GATEWAY failed, error code= {0}", iLastErr);

            }
            else
            {   //成功
                Console.WriteLine("NET_DVR_REMOTECONTROL_GATEWAY succeeded!");

            }
            Marshal.FreeHGlobal(ptrCtrlGate);
        }
        
        
        //获取配置参数
        private void AccessAppSettings()
        {
            try
            {
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                DVRIPAddress = config.AppSettings.Settings["DVRIPAddress"].Value;
                DVRPortNumber = Int16.Parse(config.AppSettings.Settings["DVRPortNumber"].Value);
                DVRUserName = config.AppSettings.Settings["DVRUserName"].Value;
                DVRPassword = config.AppSettings.Settings["DVRPassword"].Value;
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message + ex.StackTrace);
            }
        }

        //写日志
        private void WriteLog(string msg)
        {
            try
            {
                lock (fileObj)
                {
                    StreamWriter sw = new StreamWriter("D:\\1.txt");
                    sw.Write(msg);
                    sw.Close();
                    sw.Dispose();
                }
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message + ex.StackTrace);
            }
        }
        
    }
}
