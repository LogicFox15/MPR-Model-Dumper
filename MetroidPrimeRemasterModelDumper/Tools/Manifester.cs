using DKCTF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace MetroidPrimeRemasterModelDumper.Tools
{
    public static class Manifester
    {
        static List<FileInfo> RomFiles = new List<FileInfo>();
        static List<ManifestEntry> PakManifestEntry = new List<ManifestEntry>();

        public static void ProcessModels(string romDir)
        {
            DirectoryInfo DirInfo = new DirectoryInfo(@romDir);

            foreach (var subdir in DirInfo.GetDirectories())
            {
                ScanForSubdir(subdir);
            }

            foreach (var file in DirInfo.GetFiles())
            {
                ScanForFile(DirInfo);
            }

            foreach (var file in RomFiles)
            {
                string pakFile = file.FullName;
                Console.WriteLine(file.Name);
                var ctx = new AvaloniaToolbox.Core.FileContext()
                {
                    FilePath = pakFile,
                    FileName = Path.GetFileName(pakFile),
                    Stream = File.OpenRead(pakFile),
                };

                PAK pak = new PAK() { FileInfo = ctx };
                pak.Load(ctx);

                ManifestEntry entry = new ManifestEntry();
                entry.PakName = ctx.FileName;
                entry.PakPath = ctx.FilePath;

                foreach (var fileInfo in pak.files)
                {
                    entry.Files.Add(fileInfo.AssetEntry.FileID);
                    
                }

                PakManifestEntry.Add(entry);
            }

            List<ManifestSerializableEntry> SerialEntry = new List<ManifestSerializableEntry>();

            foreach (var entry in PakManifestEntry)
            {
                List<string> file = new List<string>();

                foreach (var fileEntry in entry.Files)
                {
                    file.Add(fileEntry.ToString());
                }

                var newEntry = new ManifestSerializableEntry
                {
                    PakName = entry.PakName,
                    PakPath = entry.PakPath,
                    Files = file,
                    //CMDLFiles = cmdl
                };

                SerialEntry.Add(newEntry);          
            }

            string jsonOutput = JsonSerializer.Serialize(SerialEntry, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(AppContext.BaseDirectory + "/FileManifest.json", jsonOutput);

        }

        static void ScanForSubdir(DirectoryInfo subdir)
        {
            DirectoryInfo[] subdirs = subdir.GetDirectories();

            if (subdirs.Length > 0)
            {
                //int i = 0;
                foreach (var subsubdir in subdir.GetDirectories())
                {
                    ScanForSubdir(subsubdir);
                }

                ScanForFile(subdir);
            }
            else
            {
                ScanForFile(subdir);
            }
        }

        static void ScanForFile(DirectoryInfo DirInfo)
        {
            foreach (var file in DirInfo.GetFiles())
            {
                if (file.Extension == ".pak")
                {
                    RomFiles.Add(file);
                }

            }
        }
    }

    public class ManifestEntry()
    {
        public string PakName = "";
        public string PakPath = "";
        public List<CObjectId> Files = new List<CObjectId>();
        //public List<CObjectId> CMDLFiles = new List<CObjectId>();
    }

    public class ManifestSerializableEntry()
    {
        [JsonPropertyName("PakName")]
        public string PakName { get; set; }
        [JsonPropertyName("PakPath")]
        public string PakPath { get; set; }

        [JsonPropertyName("Files")]
        public List<string> Files { get; set; }

        //[JsonPropertyName("CMDLFiles")]
        //public List<string> CMDLFiles { get; set; }
    }
}
