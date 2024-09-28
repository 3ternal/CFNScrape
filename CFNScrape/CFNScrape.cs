using System.Text.Json;
using System.Text.Json.Nodes;

CFNScrape instance = new CFNScrape();

//get the master lists from CFN (only needs to be done once)
instance.DownloadAllPlayers();

//split the lists into unique all-time players and recent players (only needs to be done once)
instance.FindUniquePlayers();
instance.FindRecentPlayers();

//calculate the totals and percentiles
instance.AnalyzeResults(CFNScrape.uniquePlayersFilename);
instance.AnalyzeResults(CFNScrape.recentPlayersFilename);

class CFNScrape
{
    #region authentication
    // find the real values from authenticated buckler site requests and add them to the Private directory (see ReadTokensFromFiles() for details)

    // to find these, open the Network tab in your browser's developer console, and log in to the Buckler site
    // there should be a fetch request called "getlogindata"
    // in the request headers, there should be a Cookie field that contains your buckler_r_id and your buckler_id
    string bucklerId = "asdfasdfasdfasdfasdfasdfasdfasdfasdfasdfasdfasdfasdfasdfasdfasdf";
    string bucklerRId = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";

    //to find the URL Token, nagivate to the rankings page on the Buckler website and click on a page number
    //there should be a request called "league.json?page=x", where x is your current page number.
    //in the headers of this request, the Request URL should contain the URL Token, which matches the format of the string used in GetUrl()
    string urlToken = "asdfasdfasdfasdf";
    #endregion

    #region other fields
    /// <summary>
    /// Default should be 1, unless you want to start from a specific rank (useful if you're resuming a previous search)
    /// </summary>
    const int startRank = 1;

    /// <summary>
    /// Default should be 1, unless you want to start from a specific page (useful if you're resuming a previous search)
    /// </summary>
    const int startPage = 1;

    public const string uniquePlayersFilename = "unique_players.jsonl";
    public const string recentPlayersFilename = "recent_players.jsonl";

    /// <summary>
    /// How many days should count as "recent" when making our list of recent players?
    /// </summary>
    const int recentlyPlayedThreshold = 90;

    //these bools may be changed
    const bool logDebug = false;

    /// <summary>
    /// If true, we'll append players to the existing rank files. If false, we'll skip a rank if its corresponding file already exists.
    /// </summary>
    const bool reuseFiles = false;
    bool skipDownloadFromCfn = false;

    /// <summary>
    /// The time (Unix timestamp) that the download operation completed. This is for calculating who has played within the past 3 months.<br></br>
    /// If you're running the analysis at a later date, remember to set this manually!
    /// </summary>
    long timeOfDownloadFromCfn = 1727028459;

    string webPageContent;
    #endregion

    #region download from CFN
    /// <summary>
    /// Scrapes a full list of players from Buckler's Boot Camp, separated by rank. This will take a while (more than 12 hours).
    /// </summary>
    public async void DownloadAllPlayers()
    {
        //optional toggle to skip downloading. use this if you only need to run the analysis.
        if (skipDownloadFromCfn)
            return;

        bool wroteAtLeastOneFile = false;

        //get the bucklerId, bucklerRId, and urlToken
        ReadTokensFromFiles();

        //set up the client
        HttpClientHandler handler = new HttpClientHandler()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        HttpClient client = new HttpClient(handler);

        //we'll need one loop for each league
        for (int currLeagueNum = startRank; currLeagueNum <= 36; currLeagueNum++)
        {
            string filename = Path.Combine("Output", GetFilename(currLeagueNum));

            //if file already exists, we might want to just skip it
            if (File.Exists(filename) && !reuseFiles)
            {
                continue;
            }

            wroteAtLeastOneFile = true;

            //now loop over the pages
            //initialize this as a non=zero value so the loop doesn't immdiately exit
            //it'll be set to the real value within the loop
            int pages = 999999;
            for (int currPageNum = startPage; currPageNum <= pages; currPageNum++)
            {
                //create a URL with your unique token
                string url = GetUrl(currLeagueNum, currPageNum);

                //set up the request
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.Add("Cookie", $"buckler_r_id={bucklerRId}; buckler_praise_date=1725728848357; buckler_id={bucklerId}");
                request.Headers.Add("Accept", "*/*");
                request.Headers.Add("Accept-Encoding", "gzip, deflate, br, zstd");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.9,ja;q=0.8");
                request.Headers.Add("Cache-Content", "no-cache");
                request.Headers.Add("Connection", "keep-alive");
                request.Headers.Add("Host", "www.streetfighter.com");
                request.Headers.Add("Pragma", "no-cache");
                request.Headers.Add("Referer", "https://www.streetfighter.com/6/buckler/ranking/league?character_filter=1&character_id=luke&platform=1&user_status=1&home_filter=1&home_category_id=0&home_id=1&league_rank=1&page=1");
                request.Headers.Add("Sec-Fetch-Dest", "empty");
                request.Headers.Add("Sec-Fetch-Mode", "cors");
                request.Headers.Add("Sec-Fetch-Site", "same-origin");
                request.Headers.Add("TE", "trailers");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:120.0) Gecko/20100101 Firefox/120.0");

                //send and await the request
                HttpResponseMessage result = client.SendAsync(request).Result;
                webPageContent = await result.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(webPageContent))
                {
                    Console.Error.WriteLine("Page content is empty!");
                    return;
                }

                bool success = result.IsSuccessStatusCode;
                if (!success)
                {
                    Console.WriteLine($"\nRequest failed\nStatus: {result.StatusCode}\nReason:{result.ReasonPhrase}\n" +
                        $"Response headers: {result.Headers}\n{result.TrailingHeaders}");

                    //the request could fail if we get blocked by the server, so we should just wait and try again
                    //if this continues to fail, you might need a new urlToken
                    Thread.Sleep(new TimeSpan(hours: 0, minutes: 10, seconds: 0));

                    currPageNum--;
                    continue;
                }

                //convert the page's raw text into json that we can navigate
                JsonNode? jsonDoc = null;

                try
                {
                    jsonDoc = JsonNode.Parse(webPageContent);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to parse JSON content\nYou may have fetched the wrong page by accident\n" +
                        "Double-check that the bucklerId, bucklerRId, and urlToken are correct\n");
                    Console.WriteLine($"{e} \n");
                    Console.WriteLine($"Content:\n{webPageContent}\n");
                    break;
                }

                if (logDebug)
                {
                    Console.WriteLine($"Content:\n{webPageContent}\n");
                }

                //get the total page count
                JsonNode? pageProps = GetJsonNode(jsonDoc, "pageProps");
                JsonNode? leaguePointRanking = GetJsonNode(pageProps, "league_point_ranking");
                JsonNode? totalPages = GetJsonNode(leaguePointRanking, "total_page");
                pages = totalPages!.GetValue<int>();

                JsonNode? playerList = GetJsonNode(leaguePointRanking, "ranking_fighter_list");

                //write each player's data to a file
                //we'll have one file per league
                foreach (JsonNode? player in playerList!.AsArray())
                {
                    //save some information about the player
                    string username = GetValueFromJson<string>(player, new List<string>() { "fighter_banner_info", "personal_info", "fighter_id" });
                    long userId = GetValueFromJson<long>(player, new List<string>() { "fighter_banner_info", "personal_info", "short_id" });
                    int leaguePoints = GetValueFromJson<int>(player, "league_point");
                    int masterRate = GetValueFromJson<int>(player, "master_rating");
                    int lastPlayedTime = GetValueFromJson<int>(player, new List<string>() { "fighter_banner_info", "last_play_at" });

                    PlayerData data = new PlayerData()
                    {
                        username = username,
                        userId = userId,

                        leaguePoints = leaguePoints,
                        masterRate = masterRate,

                        lastPlayedAtUnixTime = lastPlayedTime
                    };

                    //convert the data into a JSON string that we can write to a file
                    string jsonStr = $"{JsonSerializer.Serialize(data)}\n";

                    //this file is located in CFNScrape\bin\Debug\net6.0\Output
                    File.AppendAllText(filename, jsonStr);
                }

                Console.WriteLine($"Processing rank {currLeagueNum}, page {currPageNum} of {pages}.");
            }
        }

        //we should keep track of the time that the download was finished so that we can use it when calculating the number of recent players
        if (wroteAtLeastOneFile)
        {
            DateTimeOffset offset = new DateTimeOffset(DateTime.UtcNow);
            timeOfDownloadFromCfn = offset.ToUnixTimeSeconds();
        }
    }

    /// <summary>
    /// Reads the bucklerId, bucklerRId, and urlToken from a .txt file.<br></br>
    /// We're using an external file so that we can leave our info in there and add that folder to the gitignore.
    /// </summary>
    private void ReadTokensFromFiles()
    {
        //get the tokens from a txt file (you'll have to create this yourself)
        string bucklerIdFile = Path.Combine("Private", "bucklerId.txt");
        string bucklerRIdFile = Path.Combine("Private", "bucklerRId.txt");
        string urlTokenFile = Path.Combine("Private", "urlToken.txt");

        if (!File.Exists(bucklerIdFile))
            throw new Exception("Please create a file called bucklerId.txt in CFNScrape\\bin\\Debug\\net6.0 and paste in your Buckler ID");
        if (!File.Exists(bucklerRIdFile))
            throw new Exception("Please create a file called bucklerRId.txt in CFNScrape\\bin\\Debug\\net6.0 and paste in your Buckler RID");
        if (!File.Exists(urlTokenFile))
            throw new Exception("Please create a file called urlToken.txt in CFNScrape\\bin\\Debug\\net6.0 and paste in your URL Token");

        bucklerId = File.ReadAllText(bucklerIdFile);
        bucklerRId = File.ReadAllText(bucklerRIdFile);
        urlToken = File.ReadAllText(urlTokenFile);

        Console.WriteLine($"Buckler ID: {bucklerId}\nBuckler RId: {bucklerRId}\nURL Token Path: {urlToken}\n");
    }

    T GetValueFromJson<T>(JsonNode? parentNode, string childName)
    {
        return GetValueFromJson<T>(parentNode, new List<string>() { childName });
    }

    T GetValueFromJson<T>(JsonNode? parentNode, List<string> childNames)
    {
        JsonNode? currentNode = parentNode;

        for (int i = 0; i < childNames.Count; i++)
        {
            parentNode = currentNode;
            currentNode = parentNode![childNames[i]];
        }

        T outputValue = JsonSerializer.Deserialize<T>(currentNode);
        return outputValue;
    }

    JsonNode? GetJsonNode(JsonNode? input, string childNode)
    {
        if (input == null)
        {
            Console.WriteLine($"Content:\n{webPageContent}\n");
            throw new Exception("Input node is null");
        }

        JsonNode? output = input![childNode];

        if (output == null)
        {
            Console.WriteLine($"Content:\n{webPageContent}\n");
            throw new Exception($"Couldn't find node \"{childNode}\"");
        }

        return output;
    }

    string GetFilename(int leagueNum)
    {
        return $"rank{leagueNum:D2}.jsonl";
    }

    string GetUrl(int league, int page)
    {
        return $"https://www.streetfighter.com/6/buckler/_next/data/{urlToken}/en/ranking/league.json?character_filter=1&character_id=luke&platform=1&user_status=1&home_filter=1&home_category_id=0&home_id=1&league_rank={league}&page={page}";
    }
    #endregion

    /// <summary>
    /// Takes the full list of players from CFN and removes the duplicates while keeping the highest-ranked instance of each player.
    /// </summary>
    public void FindUniquePlayers()
    {
        string outputFile = Path.Combine("Output", uniquePlayersFilename);
        if (File.Exists(outputFile))
        {
            Console.WriteLine($"File at {outputFile} already exists, so we won't try to rewrite it");
            return;
        }

        List<PlayerData> highestRankedChars = new List<PlayerData>();

        //it makes more sense to start from the top and go down, because we're only trying to find the strongest char for each player
        for (int currLeagueNum = 36; currLeagueNum >= 1; currLeagueNum--)
        {
            string filename = Path.Combine("Output", GetFilename(currLeagueNum));
            string[] lines = File.ReadAllLines(filename);

            Console.WriteLine($"Analyzing {LeagueNumberToName(currLeagueNum)}\nUnique players found so far: {highestRankedChars.Count.ToString("N0")}\n");

            foreach (string line in lines)
            {
                PlayerData player = JsonSerializer.Deserialize<PlayerData>(line);
                
                //for convenience, we should write the player's league name to the file
                player.leagueNumber = currLeagueNum;
                player.leagueName = LeagueNumberToName(player.leagueNumber);

                //Master has its own rules because we need subcategories for MR ranges
                if (LeagueNumberToName(currLeagueNum) == "Master")
                {
                    player.masterNumber = MasterRateToNumber(player.masterRate, player.leaguePoints);
                    player.masterName = MasterNumberToName(player.masterNumber);
                }

                //if the player hasn't been added to the list before, then add them
                if (!highestRankedChars.Exists(x => x.userId == player.userId))
                {
                    highestRankedChars.Add(player);
                }

                //otherwise, only add them if the new char is higher ranked than the old char
                else
                {
                    if (LeagueNumberToName(player.leagueNumber) == "Master")
                    {
                        PlayerData oldEntry = highestRankedChars.Find(x => x.userId == player.userId);

                        if (LeagueNumberToName(oldEntry.leagueNumber) == "Master")
                        {
                            if (player.masterRate > oldEntry.masterRate)
                            {
                                highestRankedChars.Remove(oldEntry);
                                highestRankedChars.Add(player);

                                //Console.WriteLine($"Moved {player.username} from {oldEntry.masterRate} to {player.masterRate}");
                            }
                        }
                    }
                }
            }
        }

        //write the output
        foreach (PlayerData player in highestRankedChars)
        {
            string jsonStr = JsonSerializer.Serialize(player) + Environment.NewLine;
            File.AppendAllText(outputFile, jsonStr);
        }

        Console.WriteLine($"Created {uniquePlayersFilename}\n");
    }

    /// <summary>
    /// Outputs a list of unique players who've played within the last 3 months.
    /// </summary>
    public void FindRecentPlayers()
    {
        string outputFile = Path.Combine("Output", recentPlayersFilename);
        if (File.Exists(outputFile))
        {
            Console.WriteLine($"File at {outputFile} already exists, so we won't try to rewrite it");
            return;
        }

        List<PlayerData> recentPlayers = new List<PlayerData>();

        //read the list of all unique players
        string allUniquePlayersFile = Path.Combine("Output", uniquePlayersFilename);
        string[] lines = File.ReadAllLines(allUniquePlayersFile);
        
        foreach (string line in lines)
        {
            PlayerData player = JsonSerializer.Deserialize<PlayerData>(line);

            //how long has it been since the user played?
            long timeSinceLastPlayed = timeOfDownloadFromCfn - player.lastPlayedAtUnixTime;
            if (timeSinceLastPlayed < 0)
            {
                throw new Exception($"You might have forgotten to set timeOfDownloadFromCfn\nRanks scraped from CFN at {timeOfDownloadFromCfn}\n" +
                    $"{player.username} ({player.userId}) last played at {player.lastPlayedAtUnixTime}");
            }

            //if the user played within the last 90 days, add them to the recent players list
            TimeSpan span = TimeSpan.FromSeconds(timeSinceLastPlayed);
            if (span.Days <= recentlyPlayedThreshold)
            {
                recentPlayers.Add(player);
            }

            if (logDebug)
                Console.WriteLine($"{player.username} last played {span.Days} days ago");
        }

        //write to file
        foreach (PlayerData player in recentPlayers)
        {
            string jsonStr = $"{JsonSerializer.Serialize(player)}\n";
            File.AppendAllText(outputFile, jsonStr);
        }

        Console.WriteLine($"Created {recentPlayersFilename}\n");
    }

    public void AnalyzeResults(string listOfPlayersFilename)
    {
        Console.WriteLine($"\nAnalyzing results from {listOfPlayersFilename}\n");

        //read the file
        string filename = Path.Combine("Output", listOfPlayersFilename);

        if (!File.Exists(filename))
        {
            throw new Exception($"File at {filename} doesn't exist!");
        }

        string[] lines = File.ReadAllLines(filename);

        //Key = rank name, Value = number of players in that rank
        Dictionary<string, int> playersInEachRank = new Dictionary<string, int>();

        //set up the dict
        playersInEachRank = new Dictionary<string, int>();
        for (int i = 1; i <= 35; i++)
        {
            playersInEachRank.Add(LeagueNumberToName(i), 0);
        }
        for (int i = 1; i <= 12; i++)
        {
            playersInEachRank.Add(MasterNumberToName(i), 0);
        }

        //populate the dict
        int totalPlayers = 0;

        foreach (string line in lines)
        {
            PlayerData player = JsonSerializer.Deserialize<PlayerData>(line);

            totalPlayers++;

            if (player.leagueName != "Master")
                playersInEachRank[player.leagueName]++;
            else
                playersInEachRank[player.masterName]++;
        }

        //finally, print the results
        foreach (string rankName in playersInEachRank.Keys)
        {
            Console.WriteLine($"{rankName} contains {playersInEachRank[rankName]} unique players");
        }

        Console.WriteLine("\nThe following text is copy-pastable into a spreadsheet:\n");

        foreach (string rankName in playersInEachRank.Keys)
        {
            Console.WriteLine($"{playersInEachRank[rankName]}");
        }

        Console.WriteLine("\n");

        //do some math to figure out the percentiles
        for (int i = 0; i < playersInEachRank.Count; i++)
        {
            string rankName = playersInEachRank.ElementAt(i).Key;
            float percentage = (float)totalPlayers / (float)playersInEachRank[rankName];

            int numberOfPlayersBelowThisRank = 0;
            if (i > 0)
            {
                //loop over each rank below this one
                for (int j = i - 1; j >= 0; j--)
                {
                    string prevRankName = playersInEachRank.ElementAt(j).Key;
                    numberOfPlayersBelowThisRank += playersInEachRank[prevRankName];
                }
            }

            float percentile = (float)numberOfPlayersBelowThisRank / (float)totalPlayers;
            float topPercentage = 1f - percentile;

            percentile *= 100f;
            topPercentage *= 100f;

            percentile = MathF.Round(percentile, 2);
            topPercentage = MathF.Round(topPercentage, 2);

            Console.WriteLine($"{rankName} is the {percentile.ToString("F2")} percentile (top {topPercentage.ToString("F2")}%)");
        }

        Console.WriteLine($"\nTotal unique players: {totalPlayers.ToString("N0")}\n");
    }

    string LeagueNumberToName(int league)
    {
        switch (league)
        {
            case 1: return "Rookie 1";
            case 2: return "Rookie 2";
            case 3: return "Rookie 3";
            case 4: return "Rookie 4";
            case 5: return "Rookie 5";

            case 6: return "Iron 1";
            case 7: return "Iron 2";
            case 8: return "Iron 3";
            case 9: return "Iron 4";
            case 10: return "Iron 5";

            case 11: return "Bronze 1";
            case 12: return "Bronze 2";
            case 13: return "Bronze 3";
            case 14: return "Bronze 4";
            case 15: return "Bronze 5";

            case 16: return "Silver 1";
            case 17: return "Silver 2";
            case 18: return "Silver 3";
            case 19: return "Silver 4";
            case 20: return "Silver 5";

            case 21: return "Gold 1";
            case 22: return "Gold 2";
            case 23: return "Gold 3";
            case 24: return "Gold 4";
            case 25: return "Gold 5";

            case 26: return "Platinum 1";
            case 27: return "Platinum 2";
            case 28: return "Platinum 3";
            case 29: return "Platinum 4";
            case 30: return "Platinum 5";

            case 31: return "Diamond 1";
            case 32: return "Diamond 2";
            case 33: return "Diamond 3";
            case 34: return "Diamond 4";
            case 35: return "Diamond 5";

            case 36: return "Master"; //sometimes we won't use this one, because we can use the Master subdivisions instead
        }

        throw new Exception($"{league} is invalid");
    }

    string MasterNumberToName(int masterRange)
    {
        switch (masterRange)
        {
            case 1: return "Unrated";

            case 2: return "Master 1";
            case 3: return "Master 2";
            case 4: return "Master 3";
            case 5: return "Master 4";
            case 6: return "Master 5";
            case 7: return "Master 6";
            case 8: return "Master 7";
            case 9: return "Master 8";
            case 10: return "Master 9";
            case 11: return "Master 10";
            case 12: return "Master 11";
        }

        throw new Exception($"{masterRange} is invalid");
    }

    /// <summary>
    /// Figure out which Master subdivision the player belongs in, based on their MR and LP.
    /// </summary>
    /// <param name="masterRate"></param>
    /// <param name="lp"></param>
    /// <returns></returns>
    int MasterRateToNumber(int masterRate, int lp)
    {
        //if the user hasn't played for the season yet
        if (masterRate == 0)
        {
            return 1;
        }

        //or if the user is a freshly minted master
        if (masterRate == 1500 && lp < 25200)
        {
            return 1;
        }

        //otherwise, just create some arbitrary subdivisions
        if (masterRate < 1100)
            return 2;
        else if (masterRate < 1200)
            return 3;
        else if (masterRate < 1300)
            return 4;
        else if (masterRate < 1400)
            return 5;
        else if (masterRate < 1500)
            return 6;
        else if (masterRate < 1600)
            return 7;
        else if (masterRate < 1700)
            return 8;
        else if (masterRate < 1800)
            return 9;
        else if (masterRate < 1900)
            return 10;
        else if (masterRate < 2000)
            return 11;
        else
            return 12;
    }
}

[Serializable]
public class PlayerData
{
    public string username { get; set; }
    public long userId { get; set; }
    public int leaguePoints { get; set; }
    public int masterRate { get; set; }
    public int lastPlayedAtUnixTime { get; set; }

    public int leagueNumber { get; set; }
    public string leagueName { get; set; }
    public int masterNumber { get; set; }
    public string masterName { get; set; }

}