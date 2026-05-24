using DKCTF;
using EvilWithin2Tool;
using ImageLibrary;
using ImageLibrary.Formats.Encoders;
using ImageLibrary.PlatformSwizzle;
using IONET.Collada.Core.Lighting;
using RetroStudioPlugin.Files.FileData;
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

        static void ExportToPng(string outputPath, TXTR txtr)
        {
            GenericTextureBase genericTexture = new()
            {
                Width = txtr.TextureHeader.Width,
                Height = txtr.TextureHeader.Height,
                Depth = txtr.TextureHeader.Depth,
                MipCount = txtr.TextureHeader.MipCount,
                ImageFormat = new ImageFormat(FormatList[txtr.TextureHeader.Format]),
                PlatformSwizzle = new PlatformSwizzleSwitch(),
                Data = txtr.BufferData,
            };

            /*
            if (genericTexture.ImageFormat.GetEncoder() is Astc)
                genericTexture.Export(outputPath + ".astc");
            else
                genericTexture.Export(outputPath);
            */

            genericTexture.Export(outputPath);

            
            
        }

        public static Dictionary<uint, TextureFormat> FormatList = new()
        {
            {  0, TextureFormat.R8_UNORM },
            {  1, TextureFormat.R8_SNORM },
            {  2, TextureFormat.R8_UINT },
            {  3, TextureFormat.R8_SINT },
            {  4, TextureFormat.R16_UNORM },
            {  5, TextureFormat.R16_SNORM },
            {  6, TextureFormat.R16_UINT },
            {  7, TextureFormat.R16_SINT },
            {  8, TextureFormat.R16_FLOAT },
            {  9, TextureFormat.R32_UINT },
            {  10, TextureFormat.R32_SINT },
            {  11, TextureFormat.R32_FLOAT },
            {  12, TextureFormat.RGBA8_UNORM },
            {  13, TextureFormat.RGBA8_SRGB },
            {  14, TextureFormat.RGBA16_FLOAT },
            {  15, TextureFormat.RGBA32_FLOAT },
            {  16, TextureFormat.D16_UNORM },
            {  17, TextureFormat.D16_UNORM },
            {  18, TextureFormat.D24_UNORM_S8_UINT },
            {  19, TextureFormat.D32_FLOAT },
            {  20, TextureFormat.BC1_UNORM },
            {  21, TextureFormat.BC1_SRGB },
            {  22, TextureFormat.BC2_UNORM },
            {  23, TextureFormat.BC2_SRGB },
            {  24, TextureFormat.BC3_UNORM },
            {  25, TextureFormat.BC3_SRGB },
            {  26, TextureFormat.BC4_UNORM },
            {  27, TextureFormat.BC4_SNORM },
            {  28, TextureFormat.BC5_UNORM },
            {  29, TextureFormat.BC5_SNORM },
            {  30, TextureFormat.RG11B10_FLOAT },
            {  31, TextureFormat.R32_FLOAT },
            {  32, TextureFormat.RG16_FLOAT },
            {  33, TextureFormat.RG8_UNORM },
            {  34, TextureFormat.RG8_UINT },
            {  35, TextureFormat.RG8_SINT },
            {  36, TextureFormat.RG16_FLOAT },
            {  37, TextureFormat.RG16_UNORM },
            {  38, TextureFormat.RG16_SNORM },
            {  39, TextureFormat.RG16_UINT },
            {  40, TextureFormat.RG16_SINT },
            {  41, TextureFormat.RGBB10A2_UNORM },
            {  42, TextureFormat.RGB10A2_UINT },
            {  43, TextureFormat.RG32_UINT },
            {  44, TextureFormat.RG32_SINT },
            {  45, TextureFormat.RG32_FLOAT },
            {  46, TextureFormat.RGBA16_UNORM },
            {  47, TextureFormat.RGBA16_SNORM },
            {  48, TextureFormat.RGBA16_UINT },
            {  49, TextureFormat.RGBA16_SINT },
            {  50, TextureFormat.RGBA32_UINT },
            {  51, TextureFormat.RGBA32_SINT },
            {  52, TextureFormat.RGBA8_UNORM }, // None
            {  53, TextureFormat.ASTC_4x4_UNORM },
            {  54, TextureFormat.ASTC_5x4_UNORM },
            {  55, TextureFormat.ASTC_5x5_UNORM },
            {  56, TextureFormat.ASTC_6x5_UNORM },
            {  57, TextureFormat.ASTC_6x6_UNORM },
            {  58, TextureFormat.ASTC_8x5_UNORM },
            {  59, TextureFormat.ASTC_8x6_UNORM },
            {  60, TextureFormat.ASTC_8x8_UNORM },
            {  61, TextureFormat.ASTC_10x5_UNORM },
            {  62, TextureFormat.ASTC_10x6_UNORM},
            {  63, TextureFormat.ASTC_10x8_UNORM},
            {  64, TextureFormat.ASTC_10x10_UNORM},
            {  65, TextureFormat.ASTC_12x10_UNORM},
            {  66, TextureFormat.ASTC_12x12_UNORM},

            {  67, TextureFormat.ASTC_4x4_SRGB},
            {  68, TextureFormat.ASTC_5x4_SRGB },
            {  69, TextureFormat.ASTC_5x5_SRGB },
            {  70, TextureFormat.ASTC_6x5_SRGB },
            {  71, TextureFormat.ASTC_6x6_SRGB },
            {  72, TextureFormat.ASTC_8x5_SRGB },
            {  73, TextureFormat.ASTC_8x6_SRGB },
            {  74, TextureFormat.ASTC_8x8_SRGB},
            {  75, TextureFormat.ASTC_10x5_SRGB },
            {  76, TextureFormat.ASTC_10x6_SRGB},
            {  77, TextureFormat.ASTC_10x8_SRGB},
            {  78, TextureFormat.ASTC_10x10_SRGB},
            {  79, TextureFormat.ASTC_12x10_SRGB},
            {  80, TextureFormat.ASTC_12x12_SRGB},

            {  81, TextureFormat.BC6H_UF16 },
            {  82, TextureFormat.BC6H_SF16 },
            {  83, TextureFormat.BC7_UNORM },
            {  84, TextureFormat.BC7_SRGB },
        };

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
                ImageFormat = new ImageFormat(FormatList[txtr.TextureHeader.Format]),
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
            string ManifestContent = File.ReadAllText(@"MaterialManifest.json");
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
