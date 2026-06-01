using DKCTF;
using EvilWithin2Tool;
using ImageLibrary;
using ImageLibrary.Formats.Encoders;
using ImageLibrary.PlatformSwizzle;
using IONET.Collada.Core.Lighting;
using RetroStudioPlugin.Files.FileData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;
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
                Console.WriteLine("    1 = Only CHPR files");
                Console.WriteLine("    2 = Only TXTR files");
                Console.WriteLine("    3 = Only CMDL files All");
                Console.WriteLine("    4 = Only CHPR files All");
                Console.WriteLine("    5 = Only TXTR files All");

                //Console.WriteLine("    5 = Only SMDL files All (Skinned models as CMDL)");
                Console.WriteLine("");

                mode = Console.ReadLine();
            }
            else
            {
                mode = savedMode;
            }

            

            foreach (var fileInfo in pak.files)
            {
                try
                {
                    switch (mode)
                    {
                        case "0":
                            if (fileInfo.AssetEntry.Type == "CMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            break;
                        case "1":
                            if (fileInfo.AssetEntry.Type == "CHPR")
                                ExtractCharacterProject(fileInfo.FileData, fileInfo, pak);
                            break;
                        case "2":
                            if (fileInfo.AssetEntry.Type == "TXTR")
                                ExtractTXTR(fileInfo.FileData, fileInfo, pak);
                            break;
                        case "3":
                            if (fileInfo.AssetEntry.Type == "CMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "3";
                            break;
                        case "4":
                            if (fileInfo.AssetEntry.Type == "CHPR")
                                ExtractCharacterProject(fileInfo.FileData, fileInfo, pak);
                            savedMode = "4";
                            break;
                        case "5":
                            if (fileInfo.AssetEntry.Type == "TXTR")
                                ExtractTXTR(fileInfo.FileData, fileInfo, pak);
                            savedMode = "5";
                            break;
                    }
                }
                catch
                {
                    Console.WriteLine("Error with File " + fileInfo.AssetEntry.FileID);
                    Console.WriteLine("Pak Name: " + pakFile);
                    throw;
                }
                
            }
        }

        static void ExtractCharacterProject(Stream stream, FileEntry Entry, PAK pak)
        {
            Console.WriteLine("Beginning Character Project extract on file: " + Entry.AssetEntry.FileID.ToString());
            Console.WriteLine("Character Project size: " + Entry.AssetEntry.Size.ToString("X8"));
            CHPR chpr = new CHPR(stream);

            foreach (var charInfo in chpr.CharacterInfos)
            {
                foreach (var model in charInfo.ModelNodes)
                {
                    FileEntry file = new FileEntry();
                    file = SearchForModel(model.ModelFileGuid.ToString());

                    if( file == null)
                    {
                        Console.WriteLine("Error while trying to locate " + model.ModelFileGuid.ToString());
                        continue;
                    }

                    //Console.WriteLine("Located File " + model.ModelFileGuid.ToString());

                    // sub name
                    string folder = charInfo.NamePool.GetString(chpr.CharacterInfos[0].SubCharData.SubChars[0].Name);
                    //string folder = "Models";

                    // Add pak folder name onto it
                    folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder,
                        file.AssetEntry.FileID.ToString());

                    //Console.WriteLine(model.ModelFileGuid.ToString());

                    if (!Directory.Exists(folder))
                        Directory.CreateDirectory(folder);

                    var cmdl = new CMDL(file.FileData);
                    string modelName = charInfo.NamePool.GetString(model.Name);

                    string path = Path.Combine(folder, modelName);
                    CMDLExporter.Export(cmdl, path, chpr);
                    Console.WriteLine("Exported file " + file.AssetEntry.FileID.ToString());
                    Console.WriteLine("");
                    //throw new Exception("Kill application.");

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

        static void ExtractTXTR(Stream stream, FileEntry Entry, PAK pak)
        {
            var txtr = new TXTR(Entry.FileData);
            string textureName = Entry.AssetEntry.FileID.ToString();

            Console.WriteLine(txtr.TextureHeader.Format);

            GenericTextureBase genericTexture = new()
            {
                Name = textureName,
                Width = txtr.TextureHeader.Width,
                Height = txtr.TextureHeader.Height,
                ImageFormat = new ImageFormat(TXTR.FormatList[txtr.TextureHeader.Format]),
            };

            string folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath));

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string path = Path.Combine(folder, $"{textureName}.png");

            try
            {
                ExportToPng(path, txtr);
            }
            catch
            {
                if (!File.Exists(@"ErroredTextures.txt"))
                {
                    string brokenTex = textureName + "     Format: " + txtr.TextureHeader.Format;
                    File.WriteAllText(@"ErroredTextures.txt", brokenTex);
                }
                else
                {
                    string brokenTexCont = Environment.NewLine + textureName + "     Format: " + txtr.TextureHeader.Format;
                    File.AppendAllText(@"ErroredTextures.txt", brokenTexCont);
                }
                File.WriteAllBytes(Path.Combine(folder, $"{textureName}" + ".bin"), txtr.BufferData);
            }
        }

        static void ExportToPng(string outputPath, TXTR txtr)
        {
            // Type 2 = 3D Texture. If it is 3D, use Depth. Otherwise, Depth is 1.
            uint actualDepth = txtr.TextureHeader.Type == 2 ? txtr.TextureHeader.Depth : 1;
            //byte[] linearData = TXTR.Deswizzle(txtr.TextureHeader, txtr.BufferData);

            GenericTextureBase genericTexture = new()
            {
                Width = txtr.TextureHeader.Width,
                Height = txtr.TextureHeader.Height,
                Depth = actualDepth,
                MipCount = txtr.TextureHeader.MipCount,
                ImageFormat = new ImageFormat(TXTR.FormatList[txtr.TextureHeader.Format]),
                PlatformSwizzle = new PlatformSwizzleSwitch(),
                Data = txtr.BufferData,
            };
            
            genericTexture.Export(outputPath);
        }



        public static FileEntry SearchForModel(string FileID)
        {
            foreach (var fileInfo in currentPak.files)
            {
                if (fileInfo.AssetEntry.FileID.ToString() == FileID)
                {
                    Console.WriteLine("Found model: " + FileID);
                    return fileInfo;
                }
            }

            // If it reaches here, in theory, the material isn't in the pak.
            // If this is the case, time to consult the material manifest!
            Console.WriteLine(FileID.ToString() + " isn't in this pak! ");

            //System.IO.File.WriteAllText(AppContext.BaseDirectory + "/" + FileID + ".txt", FileID);

            return LocateModel(FileID);
            
        }

        public static FileEntry LocateModel(string ModelName)
        {
            string ManifestContent = File.ReadAllText(AppContext.BaseDirectory + "/ModelManifest.json");
            ModelManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<ModelManifestSerializableEntry[]>(ManifestContent);
            //Console.WriteLine("Total manifest entries: " + manifestEntries.Count());
            FileEntry TargetedFile = new FileEntry();

            bool foundFile = false;

            for (int i = 0; i < manifestEntries.Length; i++)
            {
                for (int c = 0; c < manifestEntries[i].SMDLFiles.Count(); c++)
                {
                    if (manifestEntries[i].SMDLFiles[c] == ModelName)
                    {
                        Console.WriteLine("Missing model should be in: " + manifestEntries[i].PakName);
                        TargetedFile = FetchModel(manifestEntries[i].PakPath, ModelName);
                        foundFile = true;
                        break;
                    }
                }
            }

            if (!foundFile)
            {
                Console.WriteLine("Unable to find file");
                TargetedFile = null;
            }

            return TargetedFile;
        }

        public static FileEntry FetchModel(string pakFile, string ModelName)
        {
            FileEntry TargetedModelFile = new FileEntry();

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
                    TargetedModelFile = fileInfo;
                    break;
                }
            }
            return TargetedModelFile;
        }



        // File searching because Retro Studios is darn weird with materials.
        public static FileEntry SearchForMaterial(string MaterialName, int TypeToggle)
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
            Console.WriteLine("Material file isn't in this pak. Locating file.");
            return LocateMATIFile(MaterialName);
        }

        public static FileEntry LocateMATIFile(string MaterialName)
        {
            string ManifestContent = File.ReadAllText(AppContext.BaseDirectory + "/MaterialManifest.json");
            MaterialManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<MaterialManifestSerializableEntry[]>(ManifestContent);
            //Console.WriteLine("Total manifest entries: " + manifestEntries.Count());

            FileEntry TargetedFile = new FileEntry();

            foreach(var entry in manifestEntries)
            {
                for (int c = 0; c < entry.MATIFiles.Count(); c++)
                {
                    if (entry.MATIFiles[c] == MaterialName)
                    {
                        TargetedFile = FetchMATIFile(entry.MatiPakPath, MaterialName);
                        break;
                    }
                }
            }

            return TargetedFile;
        }

        public static FileEntry FetchMATIFile(string pakFile, string MaterialName)
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


        public static string LocateTextureParentPak(string TextureName)
        {
            string ManifestContent = File.ReadAllText(AppContext.BaseDirectory + "/TextureManifest.json");
            TextureManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<TextureManifestSerializableEntry[]>(ManifestContent);
            //Console.WriteLine("Total manifest entries: " + manifestEntries.Count());
            string TargetedFileParent = null;

            bool foundFile = false;

            for (int i = 0; i < manifestEntries.Length; i++)
            {
                for (int c = 0; c < manifestEntries[i].TXTRFiles.Count(); c++)
                {
                    if (manifestEntries[i].TXTRFiles[c] == TextureName)
                    {
                        TargetedFileParent = manifestEntries[i].TxtrPakName;
                        foundFile = true;
                        break;
                    }
                }
            }

            if (!foundFile)
            {
                //Console.WriteLine("Unable to find file");
                TargetedFileParent = null;
            }

            return TargetedFileParent;
        }


    }
}
