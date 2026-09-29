using AvaloniaToolbox.Core.IO;
using DKCTF;
using EvilWithin2Tool;
using ImageLibrary;
using ImageLibrary.Formats.Encoders;
using ImageLibrary.PlatformSwizzle;
using IONET.Collada.Core.Lighting;
using MetroidPrimeRemasterModelDumper.Tools;
using RetroStudioPlugin.Files.FileData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Data.Common;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using static DKCTF.CMDL;
using static System.Runtime.InteropServices.JavaScript.JSType;
#nullable disable

namespace MetroidPrimeRemasterModelDumper
{
    public class BatchPakExtractor
    {
        public static PAK currentPak;
        public static string savedMode = "Empty";
        public static bool saveLODs = false;
        public static bool makeFolders = false;
        public static List<CMaterialNew> MaterialsNew = new List<CMaterialNew>();


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
                Console.WriteLine("Please type in the mode to run in: ");
                Console.WriteLine("");
                Console.WriteLine("    CMDL1 = Dump CMDL files (Static Models)");
                Console.WriteLine("    CMDL2 = Dump CMDL files With LODs");
                Console.WriteLine("    CHPR1 = Dump CHPR files (Rig Containers)");
                Console.WriteLine("    CHPR2 = Dump CHPR files with LODS");
                Console.WriteLine("    TXTR1 = Dump TXTR files (Textures)");
                Console.WriteLine("    TXTR2 = Dump TXTR files with folders for array textures");
                Console.WriteLine("    TERR = Dump TERR (Sol Valley Terrain Resource)");
                Console.WriteLine("    TECM = Dump TECM (Sol Valley Clip Map Resource)");
                Console.WriteLine("    ROOM = Dump ROOM and several related files");
                Console.WriteLine("");
                Console.WriteLine("WARNING: LOD identification is still buggy. Also, there are still");
                Console.WriteLine("issues with the UV maps. Dumps may not be 100% accurate.");

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
                                ExtractCharacterProject(fileInfo.FileData, fileInfo, pak);
                            savedMode = "CHPR1";
                            break;
                        case "CHPR2":
                            saveLODs = true;
                            if (fileInfo.AssetEntry.Type == "CHPR")
                                ExtractCharacterProject(fileInfo.FileData, fileInfo, pak);
                            savedMode = "CHPR2";
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
                        case "TERR":
                            if (fileInfo.AssetEntry.Type == "TERR")
                                ExtractTerrain(fileInfo.FileData, fileInfo, pak);
                            savedMode = "TERR";
                            break;
                        case "TECM":
                            if (fileInfo.AssetEntry.Type == "TECM" )
                                ExtractTerrainClipMapStitched(fileInfo.FileData, fileInfo, pak);
                            savedMode = "TECM";
                            break;
                            /*
                        case "MCON":
                            if (fileInfo.AssetEntry.Type == "MCON")
                                ProcessModConTest(fileInfo.FileData, fileInfo, pak);
                            savedMode = "MCON";
                            break;
                            */
                        case "ROOM":
                            if (fileInfo.AssetEntry.Type == "ROOM")
                                ProcessRoomTest(fileInfo.FileData, fileInfo, pak);
                            savedMode = "ROOM";
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

        #region Model Making
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
                    file = SearchForFile(model.ModelFileGuid.ToString());

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
                    CMDLExporter.Export(cmdl, path, chpr, saveLODs);
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
            CMDLExporter.Export(cmdl, path, null, saveLODs);
        }
        #endregion

        #region Texture
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

            if (makeFolders && txtr.TextureHeader.Type >= 2)
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

                path = Path.Combine(folder, $"{textureName}.png");
            }
            else
            {
                folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath));

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                path = Path.Combine(folder, $"{textureName}.png");
            }

            try
            {
                ExportTXTRToPng(path, txtr, Entry);
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

        static void ExportTXTRToPng(string outputPath, TXTR txtr, FileEntry Entry)
        {
            // Type 2 = 3D Texture. If it is 3D, use Depth. Otherwise, Depth is 1.
            uint actualDepth = txtr.TextureHeader.Type == 2 ? txtr.TextureHeader.Depth : 1;
            //Console.WriteLine("Texture Size: " + txtr.TextureSize.ToString());

            GenericTextureBase genericTexture = new GenericTextureBase();

            genericTexture.Width = txtr.TextureHeader.Width;
            genericTexture.Height = txtr.TextureHeader.Height;
            genericTexture.Depth = actualDepth;
            genericTexture.MipCount = (uint)txtr.MipSizes.Length;
            genericTexture.ImageFormat = new ImageFormat(TXTR.FormatList[txtr.TextureHeader.Format]);
            genericTexture.PlatformSwizzle = new PlatformSwizzleSwitch();
            genericTexture.Data = txtr.BufferData;

            if (txtr.TextureHeader.Type >= 2)
            {
                Console.WriteLine("Found a 3D texture. Type " + txtr.TextureHeader.Type + ".");
                genericTexture.ArrayCount = txtr.TextureHeader.Depth;
            }

            genericTexture.Export(outputPath);
        }
        #endregion

        #region Terrain
        static void ExtractTerrain(Stream stream, FileEntry Entry, PAK pak)
        {
            Console.WriteLine("");
            Console.WriteLine("WARNING: Processing the terrain format can be an intensive. For ease");
            Console.WriteLine("of usage, the terrain tiles have been broken into groups. Please ensure");
            Console.WriteLine("that your PC has a minimum of 8 gigabytes of ram before continuing with");
            Console.WriteLine("the extraction. If you plan to bring every single tile into an application");
            Console.WriteLine("such as Blender, ensure your PC has at least 8-16 gigabytes of ram.");
            Console.WriteLine("");
            Console.WriteLine("Press any key to continue.");
            Console.ReadKey();

            TERR terr = new TERR(Entry.FileData);
            Console.WriteLine("TERR successfully consumed");

            string folder = "SolValleyTerrainTiles";

            TERRExporter.ExportGLTF(terr, folder, pak);
            TERRExporter.ExportHeightmapPNG(terr, folder, pak);
            TERRExporter.ExportTextureSelectMap(terr, folder, pak);
        }

        static void ExtractTerrainClipMapStitched(Stream stream, FileEntry Entry, PAK pak)
        {
            TECM tecm = new TECM(Entry.FileData);
            Console.WriteLine("TECM successfully consumed. Attempting export...");

            string fileName = Entry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), "ClipMaps", fileName);

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            int gridDimX = (int)tecm.Header.Dimensions[0].X;
            int gridDimY = (int)tecm.Header.Dimensions[0].Y;

            // The header confirms 24 blocks per cell (96 pixels / 4)
            int blocksPerCellRow = 24;

            // Extract each active channel as its own image
            for (int c = 0; c < tecm.Header.TerrainClipMapChannels.Count; c++)
            {
                var channelDesc = tecm.Header.TerrainClipMapChannels[c];
                if (!channelDesc.Active) continue;

                int blockSize = (channelDesc.FormatType == 2) ? 8 : 16;
                int stride = blocksPerCellRow * blockSize; // bytes per block row in a single cell

                int totalWidthBlocks = gridDimX * blocksPerCellRow;
                int totalHeightBlocks = gridDimY * blocksPerCellRow;

                // Total bytes for the stitched LOD0 image
                byte[] stitchedData = new byte[totalWidthBlocks * totalHeightBlocks * blockSize];
                int currentOffset = 0;

                // Stitch row by row of the final massive grid
                for (int cellY = 0; cellY < gridDimY; cellY++)
                {
                    for (int blockRow = 0; blockRow < blocksPerCellRow; blockRow++)
                    {
                        for (int cellX = 0; cellX < gridDimX; cellX++)
                        {
                            // Assuming linear cell ordering (left-to-right, top-to-bottom)
                            int cellIndex = (cellY * gridDimX) + cellX;
                            var cell = tecm.tiles[cellIndex];

                            // Find the matching channel data in this cell
                            var chanData = cell.Channels.FirstOrDefault(ch => ch.ChannelDesc.ChannelNumber == channelDesc.ChannelNumber);

                            if (chanData != null)
                            {
                                // Calculate where in the 96x96 cell's byte array this block row starts
                                int sourceOffset = blockRow * stride;

                                // Copy the block row to our massive stitched array
                                Buffer.BlockCopy(chanData.Data, sourceOffset, stitchedData, currentOffset, stride);
                            }
                            currentOffset += stride;
                        }
                    }
                }

                // Export as DDS
                string outPath = Path.Combine(folder, $"{fileName}_Channel_{c}.dds");
                ExportTECM(outPath, stitchedData, gridDimX * 96, gridDimY * 96, channelDesc.FormatType);
                Console.WriteLine($"Exported: {outPath}");
            }
        }

        static void ExtractTerrainClipMapChunked(Stream stream, FileEntry Entry, PAK pak)
        {
            TECM tecm = new TECM(Entry.FileData);
            Console.WriteLine("TECM successfully consumed. Exporting regional chunks...");

            string fileName = Entry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), "ClipMaps", fileName, "Chunks");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            int gridDimX = (int)tecm.Header.Dimensions[0].X;
            int gridDimY = (int)tecm.Header.Dimensions[0].Y;

            // The header confirms 24 blocks per cell (96 pixels / 4)[cite: 11].
            int blocksPerCellRow = 24;

            // Define chunk size in cells (e.g., 16 cells * 96px = 1536x1536 pixel images)
            int cellsPerChunk = 16;
            int chunksX = (int)Math.Ceiling((double)gridDimX / cellsPerChunk);
            int chunksY = (int)Math.Ceiling((double)gridDimY / cellsPerChunk);

            for (int c = 0; c < tecm.Header.TerrainClipMapChannels.Count; c++)
            {
                var channelDesc = tecm.Header.TerrainClipMapChannels[c];
                if (!channelDesc.Active) continue;

                int blockSize = (channelDesc.FormatType == 2) ? 8 : 16;
                int stride = blocksPerCellRow * blockSize;

                // Iterate through the Super Tile regions
                for (int chunkY = 0; chunkY < chunksY; chunkY++)
                {
                    for (int chunkX = 0; chunkX < chunksX; chunkX++)
                    {
                        // Calculate the bounds for the current chunk
                        int startCellX = chunkX * cellsPerChunk;
                        int startCellY = chunkY * cellsPerChunk;
                        int endCellX = Math.Min(startCellX + cellsPerChunk, gridDimX);
                        int endCellY = Math.Min(startCellY + cellsPerChunk, gridDimY);

                        int chunkCellsWidth = endCellX - startCellX;
                        int chunkCellsHeight = endCellY - startCellY;

                        int chunkWidthBlocks = chunkCellsWidth * blocksPerCellRow;
                        int chunkHeightBlocks = chunkCellsHeight * blocksPerCellRow;

                        // Allocate memory ONLY for this specific chunk
                        byte[] chunkData = new byte[chunkWidthBlocks * chunkHeightBlocks * blockSize];
                        int currentOffset = 0;

                        // Stitch row by row only within the chunk boundaries
                        for (int cellY = startCellY; cellY < endCellY; cellY++)
                        {
                            for (int blockRow = 0; blockRow < blocksPerCellRow; blockRow++)
                            {
                                for (int cellX = startCellX; cellX < endCellX; cellX++)
                                {
                                    int cellIndex = (cellY * gridDimX) + cellX;
                                    var cell = tecm.tiles[cellIndex];

                                    var chanData = cell.Channels.FirstOrDefault(ch => ch.ChannelDesc.ChannelNumber == channelDesc.ChannelNumber);

                                    if (chanData != null)
                                    {
                                        int sourceOffset = blockRow * stride;
                                        Buffer.BlockCopy(chanData.Data, sourceOffset, chunkData, currentOffset, stride);
                                    }

                                    currentOffset += stride;
                                }
                            }
                        }

                        // Export this specific chunk
                        int pixelWidth = chunkCellsWidth * 96;
                        int pixelHeight = chunkCellsHeight * 96;

                        string outPath = Path.Combine(folder, $"{fileName}_Chan_{c}_Chunk_{chunkX}_{chunkY}.png");

                        ExportTECM(outPath, chunkData, pixelWidth, pixelHeight, channelDesc.FormatType);
                    }
                }
                Console.WriteLine($"Successfully exported Channel {c} chunks.");
            }
        }

        static void ExtractTerrainClipMap(Stream stream, FileEntry Entry, PAK pak)
        {
            TECM tecm = new TECM(Entry.FileData);
            Console.WriteLine("TECM successfully consumed. Exporting individual cells...");

            string fileName = Entry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), "ClipMaps", fileName, "Cells");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            int gridDimX = (int)tecm.Header.Dimensions[0].X;
            int gridDimY = (int)tecm.Header.Dimensions[0].Y;

            // Iterate through the grid coordinates instead of stitching
            for (int cellY = 0; cellY < gridDimY; cellY++)
            {
                for (int cellX = 0; cellX < gridDimX; cellX++)
                {
                    int cellIndex = (cellY * gridDimX) + cellX;
                    if (cellIndex >= tecm.tiles.Count) continue;

                    var cell = tecm.tiles[cellIndex];

                    // Export each active channel for this specific cell
                    foreach (var chanData in cell.Channels)
                    {
                        // Each cell represents a 96x96 pixel region
                        int cellPixelSize = 96;

                        string outPath = Path.Combine(folder, $"cell_{cellX}_{cellY}_chan_{chanData.ChannelDesc.ChannelNumber}.dds");
                        ExportTECM(outPath, chanData.Data, cellPixelSize, cellPixelSize, chanData.Format);
                    }
                }
            }

            Console.WriteLine($"Successfully exported individual cells to: {folder}");
        }

        static void WriteDdsFile(string path, byte[] rawBlocks, int width, int height, byte formatType)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(0x20534444); // "DDS " magic number
                bw.Write(124);        // Header size
                bw.Write(0x00081007); // Flags (caps, height, width, pixelformat, linearsize)
                bw.Write(height);
                bw.Write(width);
                bw.Write(rawBlocks.Length); // Linear size
                bw.Write(0);          // Depth
                bw.Write(0);          // Mipmap count
                for (int i = 0; i < 11; i++) bw.Write(0); // Reserved

                // Pixel Format struct (32 bytes)
                bw.Write(32);         // Size
                bw.Write(0x00000004); // Flags (DDPF_FOURCC)

                // FourCC format
                if (formatType == 2)
                    bw.Write(0x31545844); // "DXT1" / BC1
                else
                    bw.Write(0x35545844); // "DXT5" / BC3

                for (int i = 0; i < 5; i++) bw.Write(0); // RGB masks

                // Caps struct
                bw.Write(0x1000); // DDSCAPS_TEXTURE
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0); // Reserved

                // Write the stitched payload
                bw.Write(rawBlocks);
            }
        }

        static void ExportTECM(string path, byte[] rawBlocks, int width, int height, byte formatType)
        {
            GenericTextureBase genericTexture = new GenericTextureBase();
            genericTexture.Width = (uint)width;
            genericTexture.Height = (uint)height;

            uint formatToUse = 0;
            bool mortonOrder = false;
            int bytesPerBlock = 0;

            switch (formatType)
            {
                case 1:
                    formatToUse = 28;
                    bytesPerBlock = 16;
                    break;
                case 2:
                    formatToUse = 20; // BC1_UNORM
                    bytesPerBlock = 8;
                    break;
                case 3:
                    formatToUse = 28; // BC5_UNORM
                    bytesPerBlock = 16;
                    break;
            }

            genericTexture.ImageFormat = new ImageFormat(TXTR.FormatList[formatToUse]);

            genericTexture.Data = rawBlocks;
            genericTexture.Export(path);
        }


        private static uint Compact1By1(uint x)
        {
            x &= 0x55555555;
            x = (x ^ (x >> 1)) & 0x33333333;
            x = (x ^ (x >> 2)) & 0x0f0f0f0f;
            x = (x ^ (x >> 4)) & 0x00ff00ff;
            x = (x ^ (x >> 8)) & 0x0000ffff;
            return x;
        }
        #endregion

        #region Room Dumping
        static void ProcessModConTest(Stream stream, FileEntry Entry, PAK pak)
        {
            var mcon = new MCON(Entry.FileData);

            foreach(var atlast in mcon.data.visualData.visualAtlas)
            {

            }


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

            RoomInfoPrinter.PrintParsedObjects(folder, newRoom);
            RoomInfoPrinter.PrintStaticLights(folder, newRoom);
            MCONExporter.ExportRoom(newRoom, folder, false);

            Console.WriteLine("Successfully consumed a ROOM: " + Entry.AssetEntry.FileID.ToString());
        }

        /*
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
        */
        #endregion


        #region File Gathering
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
            ManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<ManifestSerializableEntry[]>(ManifestContent);
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

        public static string LocateTextureParentPak(string TextureName)
        {
            string ManifestContent = File.ReadAllText(AppContext.BaseDirectory + "/Manifest.json");
            ManifestSerializableEntry[] manifestEntries = JsonSerializer.Deserialize<ManifestSerializableEntry[]>(ManifestContent);
            //Console.WriteLine("Total manifest entries: " + manifestEntries.Count());
            string TargetedFileParent = null;

            bool foundFile = false;

            for (int i = 0; i < manifestEntries.Length; i++)
            {
                for (int c = 0; c < manifestEntries[i].Files.Count(); c++)
                {
                    if (manifestEntries[i].Files[c] == TextureName)
                    {
                        TargetedFileParent = manifestEntries[i].PakName;
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
        #endregion

        #region Debugging
        public static void DocumentModelComplexes(Stream stream, FileEntry Entry, PAK pak)
        {
            var cmdl = new CMDL(Entry.FileData);
            string modelName = Entry.AssetEntry.FileID.ToString();

            for (int i = 0; i < cmdl.Materials.Count; i++)
            {
                if (cmdl.Materials[i].HasComplex)
                {
                    if (!File.Exists(AppContext.BaseDirectory + "/ComplexDocumentation.txt"))
                    {
                        string brokenTex;
                        brokenTex = modelName + "     Type: Model     Format: " + cmdl.Materials[i].ComplexType.ToString("X8");

                        File.WriteAllText(AppContext.BaseDirectory + "/ComplexDocumentation.txt", brokenTex);
                        break;
                    }
                    else
                    {
                        string brokenTexCont;
                        brokenTexCont = Environment.NewLine + modelName + "     Type: Model     Format: " + cmdl.Materials[i].ComplexType.ToString("X8");

                        File.AppendAllText(AppContext.BaseDirectory + "/ComplexDocumentation.txt", brokenTexCont);
                        break;
                    }
                }
            }


            for (int i = 0; i < cmdl.MaterialsNew.Count; i++)
            {
                if (cmdl.MaterialsNew[i].HasComplex)

                {
                    if (!File.Exists(AppContext.BaseDirectory + "/ComplexDocumentation.txt"))
                    {
                        string brokenTex;
                        brokenTex = modelName + "     Type: Mati";
                        //brokenTex = cmdl.MaterialsNew[i].Name + "     Type: Mati";

                        File.WriteAllText(AppContext.BaseDirectory + "/ComplexDocumentation.txt", brokenTex);
                    }
                    else
                    {
                        string brokenTexCont;
                        brokenTexCont = Environment.NewLine + modelName + "     Type: Mati";
                        //brokenTexCont = Environment.NewLine + cmdl.MaterialsNew[i].Name + "     Type: Mati";

                        File.AppendAllText(AppContext.BaseDirectory + "/ComplexDocumentation.txt", brokenTexCont);
                        break;
                    }
                }
            }
        }
        #endregion

    }
}
