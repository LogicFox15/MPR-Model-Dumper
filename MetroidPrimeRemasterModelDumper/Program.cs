using MetroidPrimeRemasterModelDumper;
string manifest = AppContext.BaseDirectory + "/MaterialManifest.json";
#nullable disable

if (!File.Exists(manifest))
{
    Console.WriteLine("The material manifest does not exist. Please provide the ROMFS directory so ");
    Console.WriteLine("that a material manifest can be created. Please do not move the ROMFS once the");
    Console.WriteLine("manifest is created, as the paths to the paks will be saved for future use.");
    string romDir = Console.ReadLine();

    MaterialManifester.ProcessMP4Materials(romDir);
}

foreach (var arg in args)
{
    if (arg.EndsWith(".pak"))
    {
        BatchPakExtractor.ExtractModels(arg);
    }
}