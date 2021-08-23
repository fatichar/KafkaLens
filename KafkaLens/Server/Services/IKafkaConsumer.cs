using KafkaLens.Shared.Models;
using System.Collections.Generic;

namespace KafkaLens.Server.Services
{
    public interface IKafkaConsumer
    {
        IList<Topic> GetTopics();
    }
}