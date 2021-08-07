﻿
using AppTime.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AppTime
{
    class Recorder
    {
        public const string ExName = "mkv";

        public Recorder()
        {
            BuildDataPath();
        }

        /// <summary>
        /// 统计周期。
        /// </summary>
        public int IntervalMs = 1000;
        public void Start()
        { 
            new Thread(RecorderThreadProc) { IsBackground = true }.Start();
        }

        class App
        {
            public string WinText;
            public string AppProcess;
            public DateTime TimeStart;
            public long WinId;
        }

        public void BuildDataPath()
        {
            Directory.CreateDirectory(IconPath);
            Directory.CreateDirectory(ScreenPath);
        }

        Dictionary<int, Process> processes;
        public Process GetProcess(int processID)
        { 
            if (processes != null && processes.TryGetValue(processID, out var p))
            {
                return p;
            }

            processes = Process.GetProcesses().ToDictionary(p => p.Id);
            return processes[processID];
        }
         
        class app
        {
            public long id;
            public string process;
            public Dictionary<string, win> wins = new Dictionary<string, win>();
        }

        class win
        {
            public long id;
            public string text;
        }

        long nextAppId = 0;

        Icon GetIcon(string fileName, bool largeIcon)
        {
            var shfi = new SHFILEINFO();
            WinApi.SHGetFileInfo(fileName, 0, ref shfi,
                (uint)Marshal.SizeOf(shfi),
                (uint)FileInfoFlags.SHGFI_ICON | (uint)FileInfoFlags.SHGFI_USEFILEATTRIBUTES
                | (uint)(largeIcon ? FileInfoFlags.SHGFI_LARGEICON : FileInfoFlags.SHGFI_SMALLICON)
            );

            return Icon.FromHandle(shfi.hIcon);  
        }

        void SaveIcon(Icon icon, string filename)
        {
            using var img = icon.ToBitmap();
            img.Save(filename);
        } 

        public string GetIconPath(long appId, bool large)
        {
            return Path.Combine(IconPath, $"{appId}{(large ? "l" : "s")}.png");
        }

        Dictionary<string, app> apps = new Dictionary<string, app>();
        app GetApp(Process process)
        {
            var name = process.ProcessName;
            if (apps.TryGetValue(name, out var app))
            {
                return app;
            }

            var data = db.ExecuteDynamic(
                    @"select id from app where process = @process",
                    new SQLiteParameter("process", name)
            ).FirstOrDefault();
             
            if (data == null)
            {
                if (nextAppId == 0)
                {
                    nextAppId = (int)(long)db.ExecuteData("select ifnull(max(id),0) + 1 from app")[0][0];
                }

                var text = "";
                try
                {
                    text = process.MainModule.FileVersionInfo.FileDescription;

                    using var iconl = GetIcon(process.MainModule.FileName, true);
                    SaveIcon(iconl, GetIconPath(nextAppId, true));

                    using var icons = GetIcon(process.MainModule.FileName, false);
                    SaveIcon(icons, GetIconPath(nextAppId, false));
                }
                catch (Win32Exception)
                {
                    //ignore
                }
                catch (FileNotFoundException)
                {
                    //ignore
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    text = process.ProcessName;
                } 


                db.Execute(
                    "insert into app (id, process, text, tagId) values(@id, @process, @text, 0)",
                    new SQLiteParameter("id", nextAppId),
                    new SQLiteParameter("process", name),
                    new SQLiteParameter("text", text)
                ); 
                
                app = new app { id = nextAppId, process = name };
                nextAppId++;
            }
            else
            {
                app = new app
                {
                    id = data.id,
                    process = name
                };
            }

            apps.Add(name, app);

            //fix icons
            var largeIconPath = GetIconPath(app.id, true);
            if (!File.Exists(largeIconPath))
            {
                try
                {
                    using var iconl = GetIcon(process.MainModule.FileName, true);
                    SaveIcon(iconl, largeIconPath);

                    using var icons = GetIcon(process.MainModule.FileName, false);
                    SaveIcon(icons, GetIconPath(app.id, false));
                }
                catch(Win32Exception)
                {

                }
            }
            return app;

        }

        long nextWinId = 0;
        win GetWin(Process process, string winText)
        {
            var app = GetApp(process);
            if (app.wins.TryGetValue(winText, out var win))
            {
                return win;
            }

            var data = db.ExecuteDynamic(
                "select id from win where appid=@appid and text=@winText",
                new SQLiteParameter("appid", app.id),
                new SQLiteParameter("winText", winText)
            ).FirstOrDefault();

            if (data == null)
            {
                if (nextWinId == 0)
                {
                    nextWinId = (int)(long)db.ExecuteData("select ifnull(max(id),0) + 1 from win")[0][0];
                }
                db.Execute(
                    "insert into win (id, appId, text) values(@id, @appId, @text)",
                    new SQLiteParameter("id", nextWinId),
                    new SQLiteParameter("appId", app.id),
                    new SQLiteParameter("text", winText)
                );
                win = new win { id = nextWinId, text = winText };
                nextWinId++;
            }
            else
            {
                win = new win
                {
                    id = data.id,
                    text = winText
                };
            }

            app.wins.Add(winText, win);
            return win;
        }

        DB db = DB.Instance;

        public void RecorderThreadProc()
        {
            App lastApp = null;
            while (true)
            {
                var now = DateTime.Now; 
                var processid = 0;  // System Idle process Pid is 0
                var winText = Process.GetProcessById(processid).ProcessName;
                uint vLastInputTime = UserStatus.GetLastInputTime();
                uint idleSeconds = Settings.Default.IdleSeconds;
                bool isIdle;
                if (vLastInputTime < idleSeconds)
                {
                    var hwnd = WinApi.GetForegroundWindow();
                    var text = new StringBuilder(255);
                    WinApi.GetWindowText(hwnd, text, 255);
                    winText = text.ToString();
                    WinApi.GetWindowThreadProcessId(hwnd, out processid);
                    isIdle = false;
                }
                else
                {
                    isIdle = !Settings.Default.IdleRecord;
                    //    // Idle statistics to the Apptime
                    //    winText = Process.GetCurrentProcess().ProcessName;
                    //    processid = Process.GetCurrentProcess().Id;
                }

                var process = GetProcess(processid);
                var appname = process.ProcessName;

                // update app end time
                if (lastApp != null)
                {
                    db.Execute(
                        "update period set timeend = @v1 where timestart = @v0",
                        lastApp.TimeStart,
                        now.AddMilliseconds(-1)//必须减小，否则可能与下个周期开始时间重叠 
                    );
                }

                // set app start time
                if (lastApp == null || lastApp.AppProcess != appname || lastApp.WinText != winText)
                {

                    var win = GetWin(process, winText);
                    lastApp = new App { WinId = win.id, AppProcess = appname, TimeStart = now, WinText = winText };
                    db.Execute(
                        "insert into [period](winid, timeStart, timeEnd) values(@v0, @v1, @v1)",
                        win.id, now
                    );
                }

                if (!isIdle)
                {
                    Screenshot(now);
                }

                //等到下一个周期
                var nextTime = now.AddMilliseconds(IntervalMs);
                now = DateTime.Now;
                if(nextTime > now)
                {
                    Thread.Sleep(nextTime - now);
                } 
            }
        }
         

        string DataPath => string.IsNullOrWhiteSpace(Settings.Default.DataPath) ? Application.StartupPath : Settings.Default.DataPath;
        public string ScreenPath => Path.Combine(DataPath, "images");
        public string IconPath => Path.Combine(DataPath, "icons");
        ImageCodecInfo jpgcodec = ImageCodecInfo.GetImageDecoders().First(codec => codec.MimeType == "image/jpeg");

        ///// <summary>
        ///// 获取图片文件路径
        ///// </summary>
        ///// <param name="timeStart"></param>
        ///// <param name="timeImage"></param>
        ///// <returns></returns>
        //public string getImageFile(DateTime timeStart, DateTime timeImage)
        //{
        //    var folder = Path.Combine(ScreenPath, timeImage.ToString("yyyyMMdd"));
        //    var filename = $"{timeStart:HHmmss}+{Math.Round((timeImage - timeStart).TotalSeconds)}";
        //    return Path.Combine(folder, $"{filename}.jpg");
        //}

        public string getFileName(DateTime time)
        {
            return Path.Combine(ScreenPath, $"{time:yyyyMMdd}", $"{time:HHmmss}." + Recorder.ExName);
        }

        DateTime lastCheck = DateTime.MinValue.Date;

        /// <summary>
        /// 截图
        /// </summary>
        /// <param name="now"></param>
        /// <param name="lastApp"></param>
        void Screenshot(DateTime now)
        {
            //检查记录天数限制
            if (lastCheck != now.Date)
            {
                var firstDate = now.Date.AddDays(-Settings.Default.RecordScreenDays);
                var dirs = Directory.EnumerateDirectories(ScreenPath, "????????");
                foreach (var i in dirs)
                {
                    if (DateTime.TryParseExact(Path.GetFileName(i), "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.None, out var date))
                    {
                        if (date < firstDate)
                        {
                            Directory.Delete(i);
                        }
                    }
                }
                lastCheck = now.Date;
            }

            if (Settings.Default.RecordScreenDays == 0)
            {
                return;
            }

            if (buffer == null)
            {
                buffer = new MemoryBuffer(now);
            }

            using var img = GetScreen();
            using var mem = new MemoryStream();
            img.Save(mem, ImageFormat.Jpeg);
            buffer.Frames.Add(new Frame(now - buffer.StartTime, mem.ToArray()));
            if ((now - buffer.StartTime).TotalSeconds >= 10 * 60 || now.Date != buffer.StartTime.Date) //固定为10mins，防止保存时间长，减少出问题时影响的时长。
            {
                FlushScreenBuffer();
            }
        }


        public void FlushScreenBuffer()
        {
            //切换到新buffer
            var newBuffer = new MemoryBuffer(DateTime.Now);
            var b = buffer;
            buffer = newBuffer;

            //加入flushing
            lock (flushing)
            {
                flushing.Add(b);
            }
            var path = getFileName(b.StartTime);
            var folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            new Thread(() =>
            {
                Ffmpeg.Save(getFileName(b.StartTime), b.Frames.ToArray());
                lock (flushing)
                {
                    flushing.Remove(b);
                }
            })
            {
                Priority = ThreadPriority.BelowNormal,
                IsBackground = false
            }.Start();
        }


        public class MemoryBuffer
        {
            public readonly DateTime StartTime; 
            public readonly List<Frame> Frames = new List<Frame>();
            public MemoryBuffer(DateTime startTime)
            {
                StartTime = startTime; 
            }
        } 

        public MemoryBuffer buffer;

        public List<MemoryBuffer> flushing = new List<MemoryBuffer>(); 

        Bitmap GetScreen()
        {
            // 获取当前焦点所在的屏幕
            var screen = Screen.FromHandle(WinApi.GetForegroundWindow());
            var result = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
            using var g = Graphics.FromImage(result);
            retry:
            try
            {
                g.CopyFromScreen(screen.Bounds.X, screen.Bounds.Y, 0, 0, screen.Bounds.Size);
            }
            catch
            {
                goto retry;
            }
            return result;
        }
    }
}
