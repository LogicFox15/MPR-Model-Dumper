using MetroidPrimeRemasterModelDumper;
#nullable disable


string matManifest = AppContext.BaseDirectory + "/MaterialManifest.json";
string modelManifest = AppContext.BaseDirectory + "/ModelManifest.json";
string TextureManifest = AppContext.BaseDirectory + "/TextureManifest.json";

if (!File.Exists(matManifest) || !File.Exists(modelManifest) || !File.Exists(TextureManifest))
{
    Console.WriteLine("A manifest file does not exist. Please provide the ROMFS directory so ");
    Console.WriteLine("that the manifest files can be created. Please do not move the ROMFS once the");
    Console.WriteLine("manifest is created, as the paths to the paks will be saved for future use.");
    string romDir = Console.ReadLine();

    MaterialManifester.ProcessMP4Materials(romDir);
    ModelManifester.ProcessModels(romDir);
    TextureManifester.ProcessMP4Textures(romDir);
}


foreach (var arg in args)
{
    if (arg.EndsWith(".pak"))
    {
        try
        {
            BatchPakExtractor.ExtractModels(arg);
        }
        catch (Exception e)
        {

            Console.WriteLine(e.ToString());

            Console.Write("Press any key to continue");
            Console.ReadKey();

            throw;
        }


       
    }
}