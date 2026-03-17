using DKCTF;
using EvilWithin2Tool;
using RetroStudioPlugin.Files.FileData;
using System.Text.Json;
#nullable disable

namespace MetroidPrimeRemasterModelDumper
{
    public class BatchPakExtractor
    {
        public static PAK currentPak;
        public static string savedMode = "Empty";

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

            if (savedMode == "Empty")
            {
                Console.WriteLine("Please specify the mode to run in: ");
                Console.WriteLine("");
                Console.WriteLine("    0 = Only CMDL files");
                Console.WriteLine("    1 = Only SMDL files");
                Console.WriteLine("    2 = Both CMDL and SMDL files");
                Console.WriteLine("    3 = Only CMDL files All");
                Console.WriteLine("    4 = Only CHPR files All");
                Console.WriteLine("    5 = Both CMDL and SMDL files All");
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
                        if (fileInfo.AssetEntry.Type == "SMDL")
                            ExtractSMDL(fileInfo.FileData, fileInfo, pak);
                        break;
                    case "2":
                        if (fileInfo.AssetEntry.Type == "CMDL")
                            ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                        if (fileInfo.AssetEntry.Type == "SMDL")
                            ExtractSMDL(fileInfo.FileData, fileInfo, pak);

                        break;
                    case "3":
                        if (fileInfo.AssetEntry.Type == "CMDL")
                            ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                        savedMode = "3";
                        break;
                    case "4":
                        if (fileInfo.AssetEntry.Type == "SMDL")
                            ExtractSMDL(fileInfo.FileData, fileInfo, pak);
                        savedMode = "4";
                        break;

                    case "5":
                        if (fileInfo.AssetEntry.Type == "CMDL")
                            ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                        if (fileInfo.AssetEntry.Type == "SMDL")
                            ExtractSMDL(fileInfo.FileData, fileInfo, pak);
                        savedMode = "5";
                        break;
                }
            }
        }

        static void ExtractCharacterProject(Stream stream, FileEntry Entry, PAK pak)
        {   
            Console.WriteLine("Beginning Character Project extract");
            CHPR chpr = new CHPR(stream);

            // Load models
            foreach (var file in pak.files)
            {
                foreach (var charInfo in chpr.CharacterInfos)
                {
                    // sub name
                    //Console.WriteLine("Made it to the line where we get namepool");
                    string folder = charInfo.NamePool.GetString(chpr.CharacterInfos[0].SubCharData.SubChars[0].Name);
                    // Add pak folder name onto it
                    folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder,
                        file.AssetEntry.FileID.ToString());

                    foreach (var model in charInfo.ModelNodes)
                    {
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

        static void ExtractCMDL(Stream stream, FileEntry Entry, PAK pak)
        {
            Console.WriteLine("Asset ID: " + Entry.AssetEntry.FileID.ToString());

            var cmdl = new CMDL(Entry.FileData);
            string modelName = Entry.AssetEntry.FileID.ToString();

            //string modelName = fileEntry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(pak.FileInfo.FilePath + "working", "CMDL_" + modelName);

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
            CMDLExporter.Export(cmdl, path);
        }

        // File searching because Retro Studios is darn weird with materials.
        public static FileEntry SearchForFile(string MaterialName, int TypeToggle)
        {   
            foreach (var fileInfo in currentPak.files)
            {
                if (fileInfo.AssetEntry.FileID.ToString() == MaterialName)
                {
                    Console.WriteLine("Good news! The file is in the pak!");
                    return fileInfo;
                }
            }

            // If it reaches here, in theory, the material isn't in the pak.
            // If this is the case, time to consult the material manifest!
            Console.WriteLine("File isn't in this pak! Retro, Why?!?");
            return FetchFromManifest(MaterialName);
        }

        public static FileEntry FetchFromManifest(string MaterialName)
        {
            string ManifestContent = File.ReadAllText(@"MaterialManifest.json");
            MaterialManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<MaterialManifestSerializableEntry[]>(ManifestContent);
            Console.WriteLine("Total manifest entries: " + manifestEntries.Count());

            FileEntry TargetedFile = new FileEntry();

            foreach(var entry in manifestEntries)
            {
                for (int c = 0; c < entry.MATIFiles.Count(); c++)
                {
                    if (entry.MATIFiles[c] == MaterialName)
                    {
                        TargetedFile = LocateMATIFile(entry.PakPath, MaterialName);
                        break;
                    }
                }
            }

            return TargetedFile;
        }

        public static FileEntry LocateMATIFile(string pakFile, string MaterialName)
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
                if (fileInfo.AssetEntry.FileID.ToString() == MaterialName)
                {
                    TargetedFile = fileInfo;
                    break;
                }
            }
            return TargetedFile;
        }

    }
}
