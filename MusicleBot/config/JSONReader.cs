using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicleBot.config
{
    internal class JSONReader
    {
        public string token { get; set; }
        public string prefix { get; set; }
        public string spotifyClientId { get; set; }
        public string spotifyClientSecret { get; set; }
        public string ffmpegPath { get; set; }
        public string ytdlpPath { get; set; }
        public async Task ReadJSON()
        {
            using (StreamReader sr = new StreamReader("config.json"))
            {
                string json = await sr.ReadToEndAsync();
                JSONStructure data = JsonConvert.DeserializeObject<JSONStructure>(json);

                this.token = data.token;
                this.prefix = data.prefix;
                this.spotifyClientId = data.spotifyClientId;
                this.spotifyClientSecret = data.spotifyClientSecret;
                this.ffmpegPath = data.ffmpegPath;
                this.ytdlpPath = data.ytdlpPath;
            }
        }
    }

    internal sealed class JSONStructure
    {
        public string token { get; set; }
        public string prefix { get; set; }
        public string spotifyClientId { get; set; }
        public string spotifyClientSecret { get; set; }
        public string ffmpegPath { get; set; }
        public string ytdlpPath { get; set; }
    }
}
