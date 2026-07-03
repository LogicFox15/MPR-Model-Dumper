using AvaloniaToolbox.Core.IO;
using DKCTF;
using EvilWithin2Tool;
using ImageLibrary;
using ImageLibrary.PlatformSwizzle;
using IONET.Collada.Core.Extensibility;
using IONET.Collada.Core.Lighting;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
//using static ImageLibrary.ImageDds;

#nullable disable

namespace MetroidPrimeRemasterModelDumper
{
    public class BatchPakExtractor
    {
        public static PAK currentPak;
        public static string savedMode = "Empty";

        //public static PAK DupeData;
        public static bool saveLODs;
        public static bool makeFolders = false;

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
                Console.WriteLine("    0 = Dump CMDL files");
                Console.WriteLine("    1 = Dump CHPR files");
                Console.WriteLine("    2 = Dump WMDL files");
                Console.WriteLine("    3 = Dump CMDL files with LODs");
                Console.WriteLine("    4 = Dump CHPR files with LODs");
                Console.WriteLine("    5 = Dump WMDL files with LODs");
                Console.WriteLine("    6 = Dump TXTR files");
                Console.WriteLine("    7 = Dump TXTR files with folders for array textures");
                Console.WriteLine("");
                Console.WriteLine("WARNING: The way secondary and tertiary UVs are stored is not");
                Console.WriteLine("well understood. Some UV maps may be missing or inaccurate.");
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
                            savedMode = "0";
                            break;
                        case "1":
                            if (fileInfo.AssetEntry.Type == "CHPR")
                                ExtractCharacterProjectNew(fileInfo.FileData, pak, fileInfo);
                            savedMode = "1";
                            break;
                        case "2":
                            if (fileInfo.AssetEntry.Type == "WMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "2";
                            break;
                        case "3":
                            saveLODs = true;
                            if (fileInfo.AssetEntry.Type == "CMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "3";
                            break;
                        case "4":
                            saveLODs = true;
                            if (fileInfo.AssetEntry.Type == "CHPR")
                                ExtractCharacterProjectNew(fileInfo.FileData, pak, fileInfo);
                            savedMode = "4";
                            break;
                        case "5":
                            saveLODs = true;
                            if (fileInfo.AssetEntry.Type == "WMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "5";
                            break;
                        case "6":
                            if (fileInfo.AssetEntry.Type == "TXTR")
                                ExtractTXTR(fileInfo.FileData, fileInfo, pak);
                            savedMode = "6";
                            break;
                        case "7":
                            makeFolders = true;
                            if (fileInfo.AssetEntry.Type == "TXTR")
                                ExtractTXTR(fileInfo.FileData, fileInfo, pak);
                            savedMode = "7";
                            break;
                    }
                }
                catch
                {
                    Console.WriteLine("Error with file " + fileInfo.AssetEntry.FileID.ToString());
                    throw;
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
            CMDLExporter.Export(cmdl, path, null, saveLODs);
        }

        static void ExtractCharacterProjectNew(Stream stream, PAK pak, FileEntry fileInfo)
        {
            CHPR chpr = new CHPR(stream);

            foreach (var charInfo in chpr.CharacterInfos)
            {
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
                        CMDLExporter.Export(cmdl, path, chpr, saveLODs);
                    }
                    catch
                    {
                        break;
                    }
                    
                }
            }
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

            string folder;
            string path;

            if (makeFolders && txtr.TextureHeader.Type >= 4)
            {
                folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath));

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                folder += "/" + textureName;

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                path = Path.Combine(folder, $"{textureName}.txtr.png");
            }
            else
            {
                folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath));

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                path = Path.Combine(folder, $"{textureName}.txtr.png");
            }

            try
            {
                ExportToPng(path, txtr);
            }
            catch
            {
                if (!File.Exists(AppContext.BaseDirectory + "/ErroredTextures.txt"))
                {
                    string brokenTex = textureName + "     Format: " + txtr.TextureHeader.Format;
                    File.WriteAllText(AppContext.BaseDirectory + "/ErroredTextures.txt", brokenTex);
                }
                else
                {
                    string brokenTexCont = Environment.NewLine + textureName + "     Format: " + txtr.TextureHeader.Format;
                    File.AppendAllText(AppContext.BaseDirectory + "/ErroredTextures.txt", brokenTexCont);
                }
                File.WriteAllBytes(Path.Combine(folder, $"{textureName}" + ".bin"), txtr.BufferData);
            }
        }

        static void ExportToPng(string outputPath, TXTR txtr)
        {
            // Type 2 = 3D Texture. If it is 3D, use Depth. Otherwise, Depth is 1.
            uint actualDepth = txtr.TextureHeader.Type == 2 ? txtr.TextureHeader.Depth : 1;
            //byte[] linearData = TXTR.Deswizzle(txtr.TextureHeader, txtr.BufferData);

            Console.WriteLine("Texture Size: " + txtr.TextureSize.ToString());

            GenericTextureBase genericTexture = new GenericTextureBase();

            genericTexture.Width = txtr.TextureHeader.Width;
            genericTexture.Height = txtr.TextureHeader.Height;
            genericTexture.Depth = actualDepth;
            genericTexture.MipCount = (uint)txtr.MipSizes.Length;
            genericTexture.ImageFormat = new ImageFormat(TXTR.FormatList[txtr.TextureHeader.Format]);
            genericTexture.PlatformSwizzle = new PlatformSwizzleSwitch();
            genericTexture.Data = txtr.BufferData;

            if (txtr.TextureHeader.Type == 3)
            {
                genericTexture.ArrayCount = 6;
            }

            if (txtr.TextureHeader.Type >= 4)
            {
                Console.WriteLine("Found a 3D texture. Type " + txtr.TextureHeader.Type + ".");
                genericTexture.ArrayCount = txtr.TextureHeader.Depth;
            }

            /*
            GenericTextureBase genericTexture = new()
            {
                Width = txtr.TextureHeader.Width,
                Height = txtr.TextureHeader.Height,
                Depth = actualDepth,
                MipCount = (uint)txtr.MipSizes.Length,
                ImageFormat = new ImageFormat(TXTR.FormatList[txtr.TextureHeader.Format]),
                PlatformSwizzle = new PlatformSwizzleSwitch(),
                Data = txtr.BufferData,
            };
            */

            genericTexture.Export(outputPath);
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
            string ManifestContent = File.ReadAllText(AppContext.BaseDirectory + "/ModelManifest.json");
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
