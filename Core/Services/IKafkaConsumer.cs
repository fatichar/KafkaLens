﻿using KafkaLens.Shared.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KafkaLens.Core.Services
{
    public interface IKafkaConsumer
    {
        Task<List<Topic>> GetTopicsAsync();
        //List<Message> GetMessages(string topic, FetchOptions options);
        Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options);
        Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options);
    }
}