using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.IO;
using Newtonsoft.Json;
using System.Collections;

namespace AppSvr
{
    public class WaitPostThreadHandle
    {
        public Thread WaitPostThread;    //等待调用前置接口Post结果返回

        public Thread TcpClientThread;

        HttpListener listerner = new HttpListener();

        public System.Windows.Forms.ContainerControl m_sender = null;
        public Delegate ShowMsgEvent = null;

        public Hashtable TcpClientList = new Hashtable();

        public WaitPostThreadHandle()
        {

        }

        public void Start()
        {
            WaitPostThread = new Thread(new ThreadStart(WaitPostThreadFun));
            WaitPostThread.Name = "WaitPostThread";
            WaitPostThread.Priority = ThreadPriority.Normal;
            WaitPostThread.IsBackground = true;
            WaitPostThread.Start();

            TcpClientThread = new System.Threading.Thread(new System.Threading.ThreadStart(MaintainTcpConnect));
            TcpClientThread.Name = "MaintainTcpConnect";
            TcpClientThread.Start();
        }

        private void MaintainTcpConnect()
        {
            while (true)
            {
                DataRecvProc();
                TcpClientThread.Join(100);
            }
        }

        public void DataRecvProc()
        {
            //循环设备链表
            try
            {
                foreach (DictionaryEntry de in TcpClientList)
                {
                    try
                    {
                        ((DevTcpClient)(de.Value)).ExplainData();
                    }
                    catch (Exception e)
                    {
                        e.GetHashCode();
                        break;
                    }
                }
            }
            catch
            {

            }
        }

        public void Abort()
        {
            try
            {
                if (WaitPostThread != null)
                {
                    try
                    {
                        listerner.Stop();
                        WaitPostThread.Abort();
                        WaitPostThread = null;

                        WriteInfo("WaitPostThread abort successfully!");
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public void WriteInfo(string message)
        {
            message += "\r\n";
            if (m_sender != null) m_sender.BeginInvoke(ShowMsgEvent, new object[] { msgType.Info, message });
        }

        private void WaitPostThreadFun()
        {
            listerner = new HttpListener();

            //循环链表
            while (true)
            {
                string strLocalHttpSvrIPPort = GlbData.LocalHttpSvrIPPort;
                try
                {
                    try
                    {
                        listerner.AuthenticationSchemes = AuthenticationSchemes.Anonymous;//指定身份验证 Anonymous匿名访问

                        listerner.Prefixes.Add("http://" + strLocalHttpSvrIPPort + "/pushserver/");
                        listerner.Start();

                        WriteInfo("Http service for 'http://" + strLocalHttpSvrIPPort + "/pushserver/' start successfully! ");
                    }
                    catch
                    {
                        WriteInfo("Http service for 'http://" + strLocalHttpSvrIPPort + "/pushserver/' start failed! ");
                        break;
                    }

                    //线程池
                    int minThreadNum;
                    int portThreadNum;
                    int maxThreadNum;
                    ThreadPool.GetMaxThreads(out maxThreadNum, out portThreadNum);
                    ThreadPool.GetMinThreads(out minThreadNum, out portThreadNum);
                    while (true)
                    {
                        //等待请求连接
                        //没有请求则GetContext处于阻塞状态
                        HttpListenerContext ctx = listerner.GetContext();

                        ThreadPool.QueueUserWorkItem(new WaitCallback(TaskProc), ctx);
                    }
                }
                catch
                {

                }
                finally
                {
                }
                Thread.Sleep(5);
            }
        }

        void TaskProc(object o)
        {
            HttpListenerContext ctx = (HttpListenerContext)o;

            ctx.Response.StatusCode = 200;//设置返回给客服端http状态代码

            if (ctx.Request.HttpMethod.ToUpper().ToString() == "POST")
            {
                Stream sm;
                StreamReader reader;
                String data;

                try
                {
                    sm = ctx.Request.InputStream;//获取post正文
                    reader = new System.IO.StreamReader(sm, Encoding.UTF8);
                    data = reader.ReadToEnd();
                }
                catch
                {
                    return;
                }

                WriteInfo(DateTime.Now.ToString() + ": " + "Receieved a post request..");

                WriteInfo("Post data: " + data);

                HandlePostData(data, ctx);

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes("00");
                //对客户端输出相应信息.  
                try
                {
                    ctx.Response.ContentLength64 = buffer.Length;
                    System.IO.Stream output = ctx.Response.OutputStream;
                    output.Write(buffer, 0, buffer.Length);             //关闭输出流，释放相应资源            
                    output.Close();
                }
                catch
                { }
            }
            else
            {
                Stream sm = ctx.Request.InputStream;//获取post正文
                StreamReader reader = new System.IO.StreamReader(sm, Encoding.UTF8);
                String data = reader.ReadToEnd();

                WriteInfo("aaaaaaa data: " + data);
            }
        }

        string GetIPPort(string DevEUI, string Data)
        {
            return "127.0.0.1:12108";
        }
        void HandlePostData(string strData, HttpListenerContext ctx)
        {
            string strDeviceID = "";

            if ((strData.IndexOf("DevEUI") != -1 && strData.IndexOf("Data") != -1))
            {
                CMD_Data _Cmd;

                try
                {
                    _Cmd = JsonConvert.DeserializeObject<CMD_Data>(strData);
                }
                catch
                {
                    return;
                }

                string DevEUI = _Cmd.DevEUI.Trim();

                string Data = _Cmd.Data.Trim();

                string strIPPort = GetIPPort(DevEUI, Data);

                byte[] bFrames = new byte[Data.Length / 2 + 8];

                for (int i = 0; i < Data.Length / 2; i++)
                {
                    bFrames[i] = DevTcpClient.GetByteFromstrHex(Data.Substring(2 * i, 2));
                }

                for (int i = 0; i < 8; i++)
                {
                    bFrames[i + Data.Length / 2] = DevTcpClient.GetByteFromstrHex(DevEUI.Substring(2 *(7 - i), 2));
                }

                DevTcpClient duc;

                if (TcpClientList.ContainsKey(DevEUI))
                {
                    duc = (DevTcpClient)(TcpClientList[DevEUI]);

                    if (DateTime.Now.Subtract(duc.LastRcvTime) > new TimeSpan(0, 2, 0))
                    {
                        duc.iLastSendState = 0;
                    }

                    if (duc.iLastSendState == 0)
                    {
                        TcpClientList.Remove(DevEUI);
                        duc = new DevTcpClient(strIPPort);
                        duc.deviceID = strDeviceID;
                        duc.OpenDevice();

                        if (!TcpClientList.ContainsKey(DevEUI))
                        {
                            TcpClientList.Add(DevEUI, duc);
                        }
                        else
                        {
                            TcpClientList[DevEUI] = duc;
                        }
                    }
                    duc.WriteDevice(bFrames, 0, bFrames.Length);
                }
                else
                {
                    duc = new DevTcpClient(strIPPort);
                    duc.OpenDevice();
                    Thread.Sleep(500);

                    if (!TcpClientList.ContainsKey(DevEUI))
                        TcpClientList.Add(DevEUI, duc);

                    duc.WriteDevice(bFrames, 0, bFrames.Length);
                }
            }
        }
    }

    public class CMD_Data
    {
        private string _DevEUI, _Data;

        public string DevEUI
        {
            get { return _DevEUI; }
            set { _DevEUI = value; }
        }

        public string Data
        {
            get { return _Data; }
            set { _Data = value; }
        }
    }

}

