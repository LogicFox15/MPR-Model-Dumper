using DKCTF;
using EvilWithin2Tool;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetroidPrimeRemasterModelDumper
{
    public class BatchPakExtractor
    {
        
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

            Console.WriteLine("Please specify the mode to run in: ");
            Console.WriteLine("");
            Console.WriteLine("    0 = Only SMDL files");
            Console.WriteLine("    1 = Only CMDL files");
            Console.WriteLine("    2 = Both SMDL and CMDL files");
            Console.WriteLine("");

            string mode = Console.ReadLine();

            foreach (var fileInfo in pak.files)
            {
                switch (mode)
                {
                    case "0":
                        if (fileInfo.AssetEntry.Type == "SMDL")
                        {
                            ExtractSMDL(fileInfo.FileData, fileInfo, pak);
                        }
                        break;
                    case "1":
                        if (fileInfo.AssetEntry.Type == "CMDL")
                        {
                            ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                        }
                        break;
                    case "2":
                        if (fileInfo.AssetEntry.Type == "SMDL")
                        {
                            ExtractSMDL(fileInfo.FileData, fileInfo, pak);
                        }
                        if (fileInfo.AssetEntry.Type == "CMDL")
                        {
                            ExtractCMDL(fileInfo.FileData, fileInfo, pak);
                        }
                        break;
                    default:
                        if (fileInfo.AssetEntry.Type == "SMDL")
                        {
                            ExtractSMDL(fileInfo.FileData, fileInfo, pak);
                        }
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
            var cmdl = new CMDL(Entry.FileData);
            string modelName = Entry.AssetEntry.FileID.ToString();

            //string modelName = fileEntry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(pak.FileInfo.FilePath + "working", "CMDL" + modelName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string path = Path.Combine(folder, modelName + ".gltf");
            CMDLExporter.Export(cmdl, path);
        }

        static void ExtractSMDL(Stream stream, FileEntry Entry, PAK pak)
        {
            var cmdl = new CMDL(Entry.FileData);
            string modelName = Entry.AssetEntry.FileID.ToString();

            //string modelName = fileEntry.AssetEntry.FileID.ToString();
            string folder = Path.Combine(pak.FileInfo.FilePath + "working", "SMDL" + modelName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string path = Path.Combine(folder, modelName + ".gltf");
            CMDLExporter.Export(cmdl, path);
        }
    }
}
