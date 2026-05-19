using DKCTF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace MetroidPrimeRemasterModelDumper
{
    public static class ModelManifester
    {
        static List<FileInfo> RomFiles = new List<FileInfo>();
        static List<ModelManifestEntry> PakManifestEntry = new List<ModelManifestEntry>();

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

                ModelManifestEntry entry = new ModelManifestEntry();
                entry.PakName = ctx.FileName;
                entry.PakPath = ctx.FilePath;

                foreach (var fileInfo in pak.files)
                {
                    if (fileInfo.AssetEntry.Type == "SMDL")
                    {
                        entry.SMDLFiles.Add(fileInfo.AssetEntry.FileID);
                    }
                    /*
                    if (fileInfo.AssetEntry.Type == "CMDL")
                    {
                        entry.CMDLFiles.Add(fileInfo.AssetEntry.FileID);
                    }
                    */
                }

                PakManifestEntry.Add(entry);
            }

            List<ModelManifestSerializableEntry> SerialEntry = new List<ModelManifestSerializableEntry>();

            foreach (var entry in PakManifestEntry)
            {
                List<string> smdl = new List<string>();

                foreach (var smdlEntry in entry.SMDLFiles)
                {
                    smdl.Add(smdlEntry.ToString());
                }

                var newEntry = new ModelManifestSerializableEntry
                {
                    PakName = entry.PakName,
                    PakPath = entry.PakPath,
                    SMDLFiles = smdl,
                    //CMDLFiles = cmdl
                };

                SerialEntry.Add(newEntry);          
            }

            string jsonOutput = JsonSerializer.Serialize(SerialEntry, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            System.IO.File.WriteAllText(AppContext.BaseDirectory + "/ModelManifest.json", jsonOutput);

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

    public class ModelManifestEntry()
    {
        public string PakName = "";
        public string PakPath = "";
        public List<CObjectId> SMDLFiles = new List<CObjectId>();
        //public List<CObjectId> CMDLFiles = new List<CObjectId>();
    }

    public class ModelManifestSerializableEntry()
    {
        [JsonPropertyName("PakName")]
        public string PakName { get; set; }
        [JsonPropertyName("PakPath")]
        public string PakPath { get; set; }

        [JsonPropertyName("SMDLFiles")]
        public List<string> SMDLFiles { get; set; }

        //[JsonPropertyName("CMDLFiles")]
        //public List<string> CMDLFiles { get; set; }
    }
}
