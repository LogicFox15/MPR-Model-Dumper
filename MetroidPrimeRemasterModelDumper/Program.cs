using MetroidPrimeRemasterModelDumper;
#nullable disable

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