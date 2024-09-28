## Instructions

- Open CFNScrape.sln in Visual Studio.
- Navigate to the newly created empty folder in `CFNScrape\bin\Debug\net6.0`.
- Create a new folder called `Private`, and create three files called `bucklerId.txt`, `bucklerRId.txt`, and `urlToken.txt`.
- Log into Buckler's Boot Camp in your browser and add the appropriate information to those .txt files (details can be found in the comments of `CFNScrape.cs`).
- Click Run in Visual Studio.



## Output

If all goes well, the code will output...

- A file called `unique_players.jsonl`, which should contain a list of the highest-ranked character for every player on Buckler's Boot Camp.
- A file called `recent_players.jsonl`, which is the same as above, but it'll only include users who played within the past 90 days.
- A series of files in the format of `rank01.jsonl`, which contains the raw data pulled from Buckler's Boot Camp.

It'll also print some info about the percentiles to the console.



## Notes

- The code is roughly commented, but it might be hard to use if you're unfamiliar with programming.
- There are some variables at the top of `CFNScrape.cs` that you can change if you want (e.g. if you want to change the date range for "recent" players).
- If the scraper fails after a few hours, you might need a new `urlToken`.
