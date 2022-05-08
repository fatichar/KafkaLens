using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Utils
{
    public class Helper
    {
        public static int CompareTopics(Topic x, Topic y)
        {
            int underScoresX = CountPrefix(x.Name, '_');
            int underScoresY = CountPrefix(y.Name, '_');
            if (underScoresX == underScoresY)
            {
                return x.Name.CompareTo(y.Name);
            }
            return underScoresX < underScoresY ? -1 : +1;
        }

        private static int CountPrefix(string s, char c)
        {
            int i = 0;
            while (i < s.Length && s[i] == c)
            {
                ++i;
            }
            return i;
        }
    }
}
