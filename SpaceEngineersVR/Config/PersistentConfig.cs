using SpaceEngineersVR.Plugin;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Xml.Serialization;
using VRage.Utils;

namespace SpaceEngineersVR.Config
{
    // Adapted from Torch's Persistent<T> configuration storage.
    public class PersistentConfig<T> : IDisposable where T : class, INotifyPropertyChanged, new()
    {
        private T data;
        private Timer saveConfigTimer;
        private readonly object saveLock=new object();
        private string pending;
        private bool disposed;
        private const int SaveDelay = 500;

        private string Path
        {
            get;
        }

        public T Data
        {
            get => data;
            private set
            {
                if (data != null)
                    data.PropertyChanged -= OnPropertyChanged;

                data = value;
                data.PropertyChanged += OnPropertyChanged;
            }
        }

        private PersistentConfig(string path, T data = null)
        {
            Path = path;
            Data = data;
        }

        private void SaveLater()
        {
            lock(saveLock)
            {
                if(disposed) return;
                // Capture on the thread changing the settings. The timer only writes bytes.
                pending=Serialize();
                if(saveConfigTimer==null) saveConfigTimer=new Timer(x=>Flush());
                saveConfigTimer.Change(SaveDelay,Timeout.Infinite);
            }
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e) => SaveLater();

        public static PersistentConfig<T> Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var xmlSerializer = new XmlSerializer(typeof(T));
                    using (var streamReader = File.OpenText(path))
                        return new PersistentConfig<T>(path, (T)xmlSerializer.Deserialize(streamReader));
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load configuration file: {0}", path);
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd-hhmmss");
                    var corruptedPath = $"{path}.corrupted.{timestamp}.txt";
                    Logger.Info("Moving corrupted configuration file: {0} => {1}", path, corruptedPath);
                    File.Move(path, corruptedPath);
                }
                catch (Exception)
                {
                    // Ignored
                }
            }

            MyLog.Default.WriteLine($"SpaceEngineersVR: Writing default configuration file: {path}");
            var config = new PersistentConfig<T>(path, new T());
            config.Save();
            return config;
        }

        private string Serialize()
        {
            using(var text=new StringWriter())
            {
                new XmlSerializer(typeof(T)).Serialize(text,Data);
                return text.ToString();
            }
        }
        private void Save()
        {
            lock(saveLock) { pending=Serialize(); Flush(); }
        }
        private void Flush()
        {
            lock(saveLock)
            {
                if(pending==null) return;
                string temporary=Path+".tmp";
                try
                {
                    // Match StringWriter's UTF-16 XML declaration.
                    File.WriteAllText(temporary,pending,System.Text.Encoding.Unicode);
                    if(File.Exists(Path)) File.Replace(temporary,Path,null);
                    else File.Move(temporary,Path);
                    pending=null;
                }
                catch(Exception ex) { Logger.Warning(ex,"Could not save VR settings; keeping the previous file"); }
                finally
                {
                    try { if(File.Exists(temporary)) File.Delete(temporary); }
                    catch(IOException) { }
                }
            }
        }

        public void Dispose()
        {
            lock(saveLock)
            {
                if(disposed) return;
                disposed=true;
                Data.PropertyChanged-=OnPropertyChanged;
                saveConfigTimer?.Dispose();
                Save();
            }
        }
    }
}
