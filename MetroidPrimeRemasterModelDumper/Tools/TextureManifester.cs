using DKCTF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
#nullable disable

namespace MetroidPrimeRemasterModelDumper
{
    public static class TextureManifester
    {
        static List<FileInfo> RomFiles = new List<FileInfo>();
        static List<TextureManifestEntry> PakManifestEntry = new List<TextureManifestEntry>();

        public static void ProcessMP4Textures(string romDir)
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

            foreach(var file in RomFiles)
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

                TextureManifestEntry entry = new TextureManifestEntry();
                entry.TxtrPakName = ctx.FileName;
                entry.TxtrPakPath = ctx.FilePath;

                foreach (var fileInfo in pak.files)
                {
                    if (fileInfo.AssetEntry.Type == "TXTR")
                    {
                        entry.TXTRFiles.Add(fileInfo.AssetEntry.FileID);
                    }
                }

                PakManifestEntry.Add(entry);
            }

            List<TextureManifestSerializableEntry> SerialEntry = new List<TextureManifestSerializableEntry>();

            foreach (var entry in PakManifestEntry)
            {
                List<string> txtr = new List<string>();

                foreach(var txtrEntry in entry.TXTRFiles)
                {
                    txtr.Add(txtrEntry.ToString());
                }

                var newEntry = new TextureManifestSerializableEntry
                {
                    TxtrPakName = entry.TxtrPakName,
                    TxtrPakPath = entry.TxtrPakPath,
                    TXTRFiles = txtr,
                };

                SerialEntry.Add(newEntry);
            }

            string jsonOutput = JsonSerializer.Serialize(SerialEntry, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(AppContext.BaseDirectory + "/TextureManifest.json", jsonOutput);

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

    public class TextureManifestEntry()
    {
        public string TxtrPakName = "";
        public string TxtrPakPath = "";
        public List<CObjectId> TXTRFiles = new List<CObjectId>();
    }

    public class TextureManifestSerializableEntry()
    {
        [JsonPropertyName("PakName")]
        public string TxtrPakName { get; set; }
        [JsonPropertyName("PakPath")]
        public string TxtrPakPath { get; set; }

        [JsonPropertyName("TXTRFiles")]
        public List<string> TXTRFiles { get; set; }
    }

}
