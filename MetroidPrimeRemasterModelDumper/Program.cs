using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.Tools;
#nullable disable

string manifest = AppContext.BaseDirectory + "/FileManifest.json";

if (!File.Exists(manifest))
{
    Console.WriteLine("The file manifest does not exist. The file manifest is used to locale game files that are");
    Console.WriteLine("outside of the current package. Please paste in the path to the dumped ROMFS so that the ");
    Console.WriteLine("manifest may be created. Please do not move the ROMFS once the manifest is created, as the");
    Console.WriteLine("paths to the paks will be saved for future use.");
    string romDir = Console.ReadLine();

    Manifester.ProcessModels(romDir);
}

foreach (var arg in args)
{
    if (arg.EndsWith(".pak"))
    {
        try
        {
            BatchPakExtractor.ExtractModels(arg);
            Console.Write("Should be finished");
            Console.Write("Press any key to continue");
            Console.ReadKey();
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