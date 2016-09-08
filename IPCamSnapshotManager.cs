using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IPCamSnapshotManager
{
    public partial class IPCamSnapshotManager : ServiceBase
    {
        HashSet<String> _fileList = new HashSet<String>();
        SortedSet<String> _cacheSet = new SortedSet<String>();
        System.Timers.Timer _timer = null;
        const int DaysToKeepImages = 14;
        const int CacheDays = 2;
        const string CamDir = @"\\192.168.1.10\Public\FrontDoorCamera\";
        //const string CacheDir = @"\\192.168.1.10\Public\FrontDoorCamera\Cache\";
        const string CacheDir = @"C:\FrontDoorCameraCache\";
        const string CamUrl = @"/frontcamera/"; // relative to main page URL
        const string CacheUrl = @"/frontcache/";
        const int MaxCacheFiles = 99;
        int cacheCount = 0;


        public IPCamSnapshotManager()
        {
            InitializeComponent();

            eventLog = new System.Diagnostics.EventLog();

            if (!System.Diagnostics.EventLog.SourceExists("IPCamSnapshotManager"))
            {
                System.Diagnostics.EventLog.CreateEventSource(
                    "IPCamSnapshotManager", "Log");
            }
            eventLog.Source = "IPCamSnapshotManager";
            eventLog.Log = "Log";
        }

        protected override void OnStart(string[] args)
        {
            eventLog.WriteEntry("OnStart() called", EventLogEntryType.Information);

            // call initial setup

            // now start a timer to monitor the directory
            _timer = new System.Timers.Timer();
            _timer.Interval = 100; // initially run it immediately
            _timer.Elapsed += new System.Timers.ElapsedEventHandler(this.OnTimer);
            _timer.Start();

            //ProcessFiles(); // do it at startup rather than waiting for the first delay
        }

        protected override void OnStop()
        {
            eventLog.WriteEntry("OnStop() called", EventLogEntryType.Information);
        }

        public void OnTimer(object sender, System.Timers.ElapsedEventArgs args)
        {
            _timer.Stop();
            ProcessFiles();
        }

        private bool ParseFilename(string f, out string filename, out DateTime dt, out string shortDateTime)
        {
            // f is e.g. "\\192.168.1.10\Public\FrontDoorCamera\192.168.1.51_01_20160505160951242_LINE_CROSSING_DETECTION.jpg"

            string dateTimeString = "";
            int index = f.LastIndexOf('\\');

            dt = new DateTime();
            shortDateTime = "";

            if (index < 0)
                index = 0;

            dateTimeString = filename = f.Substring(index + 1);
            index = 0;
            index = dateTimeString.IndexOf('_', index);

            if (index < 0)
                return false;

            index = dateTimeString.IndexOf('_', index + 1);

            if (index < 0)
                return false;

            dateTimeString = dateTimeString.Substring(index + 1);

            dateTimeString = dateTimeString.Substring(0, 14);

            int year = Int32.Parse(dateTimeString.Substring(0, 4));
            int month = Int32.Parse(dateTimeString.Substring(4, 2));
            int day = Int32.Parse(dateTimeString.Substring(6, 2));
            int hour = Int32.Parse(dateTimeString.Substring(8, 2));
            int min = Int32.Parse(dateTimeString.Substring(10, 2));
            int second = Int32.Parse(dateTimeString.Substring(12, 2));

            dt = new DateTime(year, month, day, hour, min, second, DateTimeKind.Utc); // this will be an hour out from now if DST is in operation (camera UTC)

            shortDateTime = string.Format("{0}_{1}", dateTimeString.Substring(0, 8), dateTimeString.Substring(8, 6));

            return true;
        }

        private void ProcessFiles()
        {
            Console.WriteLine("ProcessFiles() start");

            var files = Directory.EnumerateFiles(CamDir, "*.jpg");

            Console.WriteLine("Enumerated " + files.Count<string>().ToString() + " files");

            DateTime now = DateTime.Now;

            try
            {
                foreach (string f in files)
                {
                    DateTime fileTime;
                    string dateTimeStr;
                    string filename;

                    if (!ParseFilename(f, out filename, out fileTime, out dateTimeStr))
                        continue;

                    if ((now - fileTime).TotalDays > DaysToKeepImages)
                    {
                        // delete
                        System.IO.File.Delete(f);
                        Console.WriteLine("Deleted " + f);

                        // remove from the file list (it won't be there if this is the first run, so check)
                        if (_fileList.Contains(f))
                            _fileList.Remove(f);

                        continue;
                    }

                    if (!_fileList.Contains(f))
                    {
                        // get creation date of file. file name generated by camera will be
                        // UTC whereas file creation time and actual time on this PC should
                        // be DST adjusted where applicable.
                        DateTime created = DateTime.MinValue;

                        try
                        {
                            created = System.IO.File.GetCreationTime(f);
                            _fileList.Add(f);
                        }
                        catch (Exception e)
                        {
                            Trace.WriteLine("Exception: " + e.InnerException);
                        }

                        string cachePathname = CacheDir + dateTimeStr + ".jpg";
                        string tempPathName = Path.GetTempPath() + dateTimeStr + ".jpg";

                        // limit how many pics we'll cache to recent ones
                        if ((now - created).TotalDays <= CacheDays)
                        {
                            // check if already in cache dir - may have been created on a previous run
                            // if not resize
                            if (!System.IO.File.Exists(cachePathname))
                            {
                                //Wrong aspect ratio, orig is 2688x1520: ResizeImage(f, tempPathName, 640, 480);
                                //ResizeImage(f, tempPathName, 672, 380);
                                ResizeImage(f, tempPathName, 336, 190);

                                cacheCount++;

                                const int MaxRetries = 3;
                                bool copied = false;

                                // copy to cache dir
                                for (int i = 0; i < MaxRetries && !copied; i++)
                                {
                                    try
                                    {
                                        File.Move(tempPathName, cachePathname);
                                        copied = true;
                                    }
                                    catch (IOException e)
                                    {
                                        // if something is using the file (AV scanner etc)
                                        System.Threading.Thread.Sleep(500);
                                        Console.WriteLine("Copy " + i + " failed (" + tempPathName + " to " + cachePathname + ")");
                                        Console.WriteLine(e.InnerException);
                                    }
                                }

                                // write meta file

                                string metaPathname = CacheDir + dateTimeStr + ".txt";

                                using (StreamWriter file = new StreamWriter(metaPathname))
                                {
                                    file.WriteLine("OriginalFile: " + CamUrl + filename);
                                    file.WriteLine("DateTime: " + fileTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                                }

                                // group pics somehow?
                            }
                        }
                    }

                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error enumerating files" + e.Message);
                eventLog.WriteEntry("Error enumerating files" + e.Message, EventLogEntryType.Error);
            }

            if (_timer != null)
            {
                _timer.Interval = 30000; // do again in 30s
                _timer.Start();
            }

            Console.WriteLine("ProcessFiles() end");
        }

        private void PruneCacheFiles()
        {
            // get a list of all the files

            var files = Directory.EnumerateFiles(CacheDir, "*.jpg");

            try
            {
                // maintain a sorted set of them, only add if we've not already seen the file
                foreach (string f in files)
                {
                    if (!_cacheSet.Contains(f))
                        _cacheSet.Add(f);
                }

                // now check if we need to remove any
                int toRemove = MaxCacheFiles - _cacheSet.Count;

                if (toRemove > 0)
                {
                    for (int i = 0; i < toRemove; i++)
                    {
                        string jpgFile = _cacheSet.ElementAt<String>(0);
                        _cacheSet.Remove(jpgFile);
                       // _cacheSet.Remove(txt);

                    }
                }

            }
            catch (Exception e)
            {

            }
        }

        private void ResizeImage(string imageFile, string outputFile, int width, int height)
        {
            Console.WriteLine("ResizeImage() " + imageFile);
            try
            {
                using (var srcImage = Image.FromFile(imageFile))
                {
                    using (var newImage = new Bitmap(width, height))
                    using (var graphics = Graphics.FromImage(newImage))
                    {
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(srcImage, new Rectangle(0, 0, width, height));

                        // this, or...

                        using (MemoryStream memory = new MemoryStream())
                        {
                            using (FileStream fs = new FileStream(outputFile, FileMode.Create, FileAccess.ReadWrite))
                            {
                                newImage.Save(memory, ImageFormat.Jpeg);
                                byte[] bytes = memory.ToArray();
                                fs.Write(bytes, 0, bytes.Length);
                                fs.Close();
                            }
                        }

                        // this caused exceptions in GDI+?
                        //newImage.Save(outputFile, ImageFormat.Jpeg);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message + " " + e.InnerException.Message);
            }
        }

        // this is to allow the service to run as a command line app
        internal void TestStartupAndStop()
        {
            this.OnStart(null);
            Console.ReadLine();
            this.OnStop();
        }
    }
}

