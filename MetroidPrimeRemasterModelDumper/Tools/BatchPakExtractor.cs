using AvaloniaToolbox.Core.IO;
using DKCTF;
using EvilWithin2Tool;
using ImageLibrary;
using ImageLibrary.PlatformSwizzle;
using IONET.Collada.Core.Extensibility;
using IONET.Collada.Core.Lighting;
using MetroidPrimeRemasterModelDumper.Tools;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
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
                Console.WriteLine("    CMDL1 = Dump CMDL files");
                Console.WriteLine("    CMDL2 = Dump CMDL files with LODs");
                Console.WriteLine("    CHPR1 = Dump CHPR files");
                Console.WriteLine("    CHPR2 = Dump CHPR files with LODs");
                Console.WriteLine("    WMDL1 = Dump WMDL files");
                Console.WriteLine("    WMDL2 = Dump WMDL files with LODs");
                Console.WriteLine("    TXTR1 = Dump TXTR files");
                Console.WriteLine("    TXTR2 = Dump TXTR files with folders for array textures");
                Console.WriteLine("    LTPB = Light Probe Texture Bundle");
                Console.WriteLine("    MCON = MCON test");
                Console.WriteLine("    ROOM = Room test");
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
                        case "CMDL1":
                            if (fileInfo.AssetEntry.Type == "CMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "CMDL1";
                            break;
                        case "CMDL2":
                            saveLODs = true;
                            if (fileInfo.AssetEntry.Type == "CMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "CMDL2";
                            break;
                        case "CHPR1":
                            if (fileInfo.AssetEntry.Type == "CHPR")
                                ExtractCharacterProjectNew(fileInfo.FileData, pak, fileInfo);
                            savedMode = "CHPR1";
                            break;
                        case "CHPR2":
                            saveLODs = true;
                            if (fileInfo.AssetEntry.Type == "CHPR")
                                ExtractCharacterProjectNew(fileInfo.FileData, pak, fileInfo);
                            savedMode = "CHPR2";
                            break;
                        case "WMDL1":
                            if (fileInfo.AssetEntry.Type == "WMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "WMDL1";
                            break;
                        case "WMDL2":
                            saveLODs = true;
                            if (fileInfo.AssetEntry.Type == "WMDL")
                                ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                            savedMode = "WMDL2";
                            break;
                        case "TXTR1":
                            if (fileInfo.AssetEntry.Type == "TXTR")
                                ExtractTXTR(fileInfo.FileData, fileInfo, pak);
                            savedMode = "TXTR1";
                            break;
                        case "TXTR2":
                            makeFolders = true;
                            if (fileInfo.AssetEntry.Type == "TXTR")
                                ExtractTXTR(fileInfo.FileData, fileInfo, pak);
                            savedMode = "TXTR2";
                            break;
                        case "LTPB":
                            if (fileInfo.AssetEntry.Type == "LTPB")
                                ExtractLTPB(fileInfo.FileData, fileInfo, pak);
                            savedMode = "LTPB";
                            break;
                        case "MCON":
                            makeFolders = true;
                            if (fileInfo.AssetEntry.Type == "MCON")
                                ProcessModConTest(fileInfo.FileData, fileInfo, pak);
                            savedMode = "MCON";
                            break;
                        case "ROOM":
                            if (fileInfo.AssetEntry.Type == "ROOM")
                                ProcessRoomTest(fileInfo.FileData, fileInfo, pak);
                            savedMode = "ROOM";
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

        
        #region Initial model dumping stuff
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
                        FileEntry file = SearchForFile(model.ModelFileGuid.ToString());

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
        #endregion

        #region Room Dumping
        static void ProcessModConTest(Stream stream, FileEntry Entry, PAK pak)
        {
            var mcon = new MCON(Entry.FileData);
            Console.WriteLine("Successfully consumed a MCON: " + Entry.AssetEntry.FileID.ToString());
        }

        static void ProcessRoomTest(Stream stream, FileEntry Entry, PAK pak)
        {
            ROOM room = new ROOM(Entry.FileData);

            ConstructedRoom newRoom = ConstructedRoom.ProcessRoomForConstruction(room);

            string roomName = Entry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), roomName);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            CMDLExporterNew.ExportRoom(newRoom, folder, false);

            Console.WriteLine("Successfully consumed a ROOM: " + Entry.AssetEntry.FileID.ToString());
        }

        static void ExtractLTPB(Stream stream, FileEntry Entry, PAK pak)
        {
            var ltpb = new LTPB(Entry.FileData);
            string lightProbeName = Entry.AssetEntry.FileID.ToString();

            string folder = Path.Combine(
                Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath),
                "LTPB_" + lightProbeName);

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            for (int i = 0; i < ltpb.lightProbeBundles.Count; i++)
            {
                var bundle = ltpb.lightProbeBundles[i];
                string textureName = $"{lightProbeName}_{i:D4}";
                string path = Path.Combine(folder, $"{textureName}.txtr.png");

                try
                {
                    ExportLTPBToPng(path, bundle.texture);
                }
                catch
                {
                    File.AppendAllText(
                        Path.Combine(folder, "ErroredTextures.txt"),
                        $"{Environment.NewLine}{textureName}     Format: {bundle.texture.TextureHeader.Format}");
                    File.WriteAllBytes(
                        Path.Combine(folder, $"{textureName}.bin"),
                        bundle.texture.BufferData ?? Array.Empty<byte>());
                }
            }

            Console.WriteLine(
                $"LTPB {lightProbeName}: parsed {ltpb.lightProbeBundles.Count} embedded textures.");
        }
        #endregion


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

            if(txtr.TextureHeader.Type >= 2)
            {
                genericTexture.ArrayCount = txtr.TextureHeader.Depth;
            }

            genericTexture.Export(outputPath);
        }

        static void ExportLTPBToPng(string outputPath, TXTR txtr)
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

            genericTexture.Export(outputPath);
        }

        #region File gathering
        public static FileEntry SearchForFile(string FileID)
        {
            foreach (var fileInfo in currentPak.files)
            {
                if (fileInfo.AssetEntry.FileID.ToString() == FileID)
                {
                    return fileInfo;
                }
            }

            // If it reaches here, in theory, the file isn't in the pak.
            // If this is the case, time to consult the manifest!

            return LocateFile(FileID);
        }

        public static FileEntry LocateFile(string ModelName)
        {
            string ManifestContent = File.ReadAllText(AppContext.BaseDirectory + "/FileManifest.json");
            MaterialManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<MaterialManifestSerializableEntry[]>(ManifestContent);
            //Console.WriteLine("Total manifest entries: " + manifestEntries.Count());

            FileEntry TargetedFile = new FileEntry();

            foreach (var entry in manifestEntries)
            {
                for (int c = 0; c < entry.Files.Count(); c++)
                {
                    if (entry.Files[c] == ModelName)
                    {
                        TargetedFile = FetchFile(entry.PakPath, ModelName);
                        break;
                    }
                }
            }

            return TargetedFile;
        }

        public static FileEntry FetchFile(string pakFile, string ModelName)
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
        #endregion

        #region Debugging
        static void GetRoomIDAndName(Stream stream, FileEntry Entry, PAK pak)
        {

            foreach (var tag in pak.PakData.NameTagEntries)
            {
                Console.WriteLine(tag.FileID.Type);

                if (tag.FileID.Type == "MOOR") // because this is backwards for some reason
                {
                    if (!File.Exists(AppContext.BaseDirectory + "/RoomIDs.txt"))
                    {
                        string brokenTex;
                        brokenTex = tag.Name.ToString() + " = " + tag.FileID.Objectid.ToString() + ",";

                        File.WriteAllText(AppContext.BaseDirectory + "/RoomIDs.txt", brokenTex);
                        break;
                    }
                    else
                    {
                        string brokenTexCont;
                        brokenTexCont = Environment.NewLine + tag.Name.ToString() + " = " + tag.FileID.Objectid.ToString() + ",";

                        File.AppendAllText(AppContext.BaseDirectory + "/RoomIDs.txt", brokenTexCont);
                        break;
                    }
                }
            }
        }
        #endregion
    }
}
