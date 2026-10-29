namespace Narrative.App
{
    public class Program
    {
        public static int Main(string[] args)
        {
            return Cli.Run(args, Console.Out, Console.Error);
        }
    }
}
