using MetroidPrimeRemasterModelDumper;

string manifest = AppContext.BaseDirectory + "/ModelManifest.json";

if (!File.Exists(manifest))
{
    Console.WriteLine("The model manifest does not exist. Please provide the ROMFS directory so ");
    Console.WriteLine("that a model manifest can be created. Please do not move the ROMFS once the");
    Console.WriteLine("manifest is created, as the paths to the paks will be saved for future use.");
    string romDir = Console.ReadLine();

    ModelManifester.ProcessModels(romDir);
}

foreach (var arg in args)
{
    if (arg.EndsWith(".pak"))
    {
        BatchPakExtractor.ExtractModels(arg);
    }
}