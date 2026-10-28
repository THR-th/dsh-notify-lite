using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DshNotifyPet
{
    public sealed class PetPreferences
    {
        private const int MaximumBytes = 8192;
        public int Volume { get; set; }
        public bool Muted { get; set; }
        public double Scale { get; set; }
        public bool Animations { get; set; }
        public int CompletedSeconds { get; set; }
        public int WaitingSeconds { get; set; }

        public PetPreferences() { Reset(); }

        public void Reset()
        {
            Volume = 60;
            Muted = false;
            Scale = 1.0;
            Animations = true;
            CompletedSeconds = 12;
            WaitingSeconds = 20;
        }

        public void Sanitize()
        {
            Volume = Math.Max(0, Math.Min(100, Volume));
            Scale = double.IsNaN(Scale) || double.IsInfinity(Scale) ? 1.0 : Math.Max(0.75, Math.Min(1.4, Scale));
            CompletedSeconds = Math.Max(5, Math.Min(60, CompletedSeconds));
            WaitingSeconds = Math.Max(5, Math.Min(60, WaitingSeconds));
        }

        public static PetPreferences Load(string path)
        {
            try
            {
                byte[] bytes = new byte[MaximumBytes + 1];
                int count = 0;
                using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (input.Length > MaximumBytes) return new PetPreferences();
                    while (count < bytes.Length)
                    {
                        int read = input.Read(bytes, count, bytes.Length - count);
                        if (read == 0) break;
                        count += read;
                    }
                }
                if (count > MaximumBytes) return new PetPreferences();
                string json = new UTF8Encoding(false, true).GetString(bytes, 0, count);
                if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 8 };
                Dictionary<string, object> values = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (values == null) return new PetPreferences();
                PetPreferences prefs = new PetPreferences();
                object value;
                if (values.TryGetValue("Volume", out value)) prefs.Volume = ReadInteger(value);
                if (values.TryGetValue("Muted", out value)) prefs.Muted = ReadBoolean(value);
                if (values.TryGetValue("Scale", out value)) prefs.Scale = ReadNumber(value);
                if (values.TryGetValue("Animations", out value)) prefs.Animations = ReadBoolean(value);
                if (values.TryGetValue("CompletedSeconds", out value)) prefs.CompletedSeconds = ReadInteger(value);
                if (values.TryGetValue("WaitingSeconds", out value)) prefs.WaitingSeconds = ReadInteger(value);
                prefs.Sanitize();
                return prefs;
            }
            catch { return new PetPreferences(); }
        }

        public bool Save(string path)
        {
            string temporary = null;
            try
            {
                Sanitize();
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                if (String.IsNullOrEmpty(directory)) return false;
                Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 8 };
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(serializer.Serialize(this));
                if (bytes.Length > MaximumBytes) return false;
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
                temporary = null;
                return true;
            }
            catch { return false; }
            finally
            {
                if (temporary != null)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch { }
                }
            }
        }

        private static bool ReadBoolean(object value)
        {
            if (!(value is bool)) throw new FormatException();
            return (bool)value;
        }

        private static double ReadNumber(object value)
        {
            if (!(value is int) && !(value is long) && !(value is decimal) && !(value is double)) throw new FormatException();
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static int ReadInteger(object value)
        {
            double number = ReadNumber(value);
            if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Truncate(number)) throw new FormatException();
            if (number > Int32.MaxValue) return Int32.MaxValue;
            if (number < Int32.MinValue) return Int32.MinValue;
            return (int)number;
        }
    }
}