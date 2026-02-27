using AvaloniaToolbox.Core.IO;
using DKCTF;
using EvilWithin2Tool;
using IONET.Collada.Core.Extensibility;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable disable

namespace MetroidPrimeRemasterModelDumper
{
    

    public class BatchPakExtractor
    {
        public static PAK currentPak;
        public static string savedMode = "Empty";

        //public static PAK DupeData;

        public static void ExtractModels(string pakFile)
        {
            var ctx = new AvaloniaToolbox.Core.FileContext()
            {
                FilePath = pakFile,
                FileName = Path.GetFileName(pakFile),
                Stream = File.OpenRead(pakFile),
            };

            PAK pak = new PAK() { FileInfo = ctx };
            pak.Load(ctx);

            currentPak = pak;

            string mode;

            if(savedMode == "Empty")
            {
                Console.WriteLine("Please specify the mode to run in: ");
                Console.WriteLine("");
                Console.WriteLine("    0 = Only CMDL files");
                Console.WriteLine("    1 = Only CHPR files");
                Console.WriteLine("    2 = Only CMDL files All");
                Console.WriteLine("    3 = Only CHPR files All");
                Console.WriteLine("");

                mode = Console.ReadLine();
            }
            else
            {
                mode = savedMode;
            }

            foreach (var fileInfo in pak.files)
            {
                switch (mode)
                {
                    case "0":
                        if (fileInfo.AssetEntry.Type == "CMDL")
                            ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                        break;
                    case "1":
                        if (fileInfo.AssetEntry.Type == "CHPR")
                            ExtractCharacterProjectNew(fileInfo.FileData, pak, fileInfo);
                        break;
                    case "2":
                        if (fileInfo.AssetEntry.Type == "CMDL")
                            ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                        savedMode = "2";
                        break;
                    case "3":
                        if (fileInfo.AssetEntry.Type == "CHPR")
                            ExtractCharacterProjectNew(fileInfo.FileData, pak, fileInfo);
                        savedMode = "3";
                        break;
                }
            }
        }

        static void ExtractCMDL(Stream stream, FileEntry Entry, PAK pak)
        {
            Console.WriteLine("Asset ID: " + Entry.AssetEntry.FileID.ToString());

            var cmdl = new CMDL(Entry.FileData);
            string modelName = Entry.AssetEntry.FileID.ToString();

            //string modelName = fileEntry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), "CMDL_" + modelName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string path = Path.Combine(folder, modelName);
            CMDLExporter.Export(cmdl, path);
        }

        static void ExtractSMDL(Stream stream, FileEntry Entry, PAK pak)
        {
            Console.WriteLine("Asset ID: " + Entry.AssetEntry.FileID.ToString());

            var cmdl = new CMDL(Entry.FileData);
            string modelName = Entry.AssetEntry.FileID.ToString();

            //string modelName = fileEntry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(pak.FileInfo.FilePath + "working", "SMDL_" + modelName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string path = Path.Combine(folder, modelName);
            CMDLExporter.Export(cmdl, folder);
        }

        static void ExtractCharacterProject(Stream stream, PAK pak, FileEntry fileInfo)
        {
            CHPR chpr = new CHPR(stream);

            foreach (var charInfo in chpr.CharacterInfos)
            {
                // Load models
                foreach (var file in pak.files)
                {
                    // sub name
                    string folder = charInfo.NamePool.GetString(chpr.CharacterInfos[0].SubCharData.SubChars[0].Name);
                    // Add pak folder name onto it
                    folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder,
                        file.AssetEntry.FileID.ToString());

                    foreach (var model in charInfo.ModelNodes)
                    {
                        //Console.WriteLine(model.ModelFileGuid.ToString());

                        if (file.FileName.Contains(model.ModelFileGuid.ToString()))
                        {
                            if (!Directory.Exists(folder))
                                Directory.CreateDirectory(folder);

                            var cmdl = new CMDL(file.FileData);
                            string modelName = charInfo.NamePool.GetString(model.Name);

                            string path = Path.Combine(folder, modelName + ".gltf");
                            CMDLExporter.Export(cmdl, path, chpr);
                        }
                    }
                }
            }
        }

        static void ExtractCharacterProjectNew(Stream stream, PAK pak, FileEntry fileInfo)
        {
            CHPR chpr = new CHPR(stream);

            foreach (var charInfo in chpr.CharacterInfos)
            {
                /*
                // sub name
                string folder = charInfo.NamePool.GetString(chpr.CharacterInfos[0].SubCharData.SubChars[0].Name);

                // Add pak folder name onto it
                folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder,
                    file.AssetEntry.FileID.ToString());
                */

                foreach (var model in charInfo.ModelNodes)
                {
                    try
                    {
                        FileEntry file = SearchForModel(model.ModelFileGuid.ToString());

                        // sub name
                        string folder = charInfo.NamePool.GetString(chpr.CharacterInfos[0].SubCharData.SubChars[0].Name);

                        // Add pak folder name onto it
                        folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder,
                            file.AssetEntry.FileID.ToString());

                        //Console.WriteLine(model.ModelFileGuid.ToString());

                        if (!Directory.Exists(folder))
                            Directory.CreateDirectory(folder);

                        var cmdl = new CMDL(file.FileData);
                        string modelName = charInfo.NamePool.GetString(model.Name);

                        string path = Path.Combine(folder, modelName + ".gltf");
                        CMDLExporter.Export(cmdl, path, chpr);
                    }
                    catch
                    {
                        break;
                    }
                    
                }
            }
        }


        public static FileEntry SearchForModel(string FileID)
        {
            foreach (var fileInfo in currentPak.files)
            {
                if (fileInfo.AssetEntry.FileID.ToString() == FileID)
                {
                    return fileInfo;
                }
            }

            // If it reaches here, in theory, the material isn't in the pak.
            // If this is the case, time to consult the material manifest!
            Console.WriteLine(FileID + " isn't in this pak! Retro, Why?!?");

            //System.IO.File.WriteAllText(AppContext.BaseDirectory + "/" + FileID + ".txt", FileID);

            return LocateModel(FileID);
        }


        public static FileEntry LocateModel(string ModelName)
        {
            string ManifestContent = File.ReadAllText(@"ModelManifest.json");
            MaterialManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<MaterialManifestSerializableEntry[]>(ManifestContent);
            Console.WriteLine("Total manifest entries: " + manifestEntries.Count());

            FileEntry TargetedFile = new FileEntry();

            foreach (var entry in manifestEntries)
            {
                for (int c = 0; c < entry.SMDLFiles.Count(); c++)
                {
                    if (entry.SMDLFiles[c] == ModelName)
                    {
                        TargetedFile = FetchModel(entry.PakPath, ModelName);
                        break;
                    }
                }
            }

            return TargetedFile;
        }

        public static FileEntry FetchModel(string pakFile, string ModelName)
        {
            FileEntry TargetedFile = new FileEntry();

            var ctx = new AvaloniaToolbox.Core.FileContext()
            {
                FilePath = pakFile,
                FileName = Path.GetFileName(pakFile),
                Stream = File.OpenRead(pakFile),
            };

            PAK pak = new PAK() { FileInfo = ctx };
            pak.Load(ctx);

            foreach (var fileInfo in pak.files)
            {
                if (fileInfo.AssetEntry.FileID.ToString() == ModelName)
                {
                    TargetedFile = fileInfo;
                    break;
                }
            }
            return TargetedFile;
        }

    }
}
