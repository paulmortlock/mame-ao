﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace mame_ao.source
{
	public class GitHubRepo
	{
		public string UserName;
		public string RepoName;

		public string UrlDetails;
		public string UrlApi;
		public string UrlRaw;

		private readonly string UrlAPiBase = "https://api.github.com";

		private dynamic DataRepo = null;

		public string tag_name = null;
		public DateTime published_at;

		public Dictionary<string, string> Assets = new Dictionary<string, string>();

		public GitHubRepo(string userName, string repoName)
		{
			UserName = userName;
			RepoName = repoName;

			UrlDetails = $"https://github.com/{userName}/{repoName}";
			UrlApi = $"{UrlAPiBase}/repos/{userName}/{repoName}";
			UrlRaw = $"https://raw.githubusercontent.com/{userName}/{repoName}";

			Initialize();
		}

		private void Initialize()
		{
			DataRepo = ApiFetch(UrlApi);

			if (DataRepo == null)
				return;

			string releases_url = DataRepo.releases_url;
			releases_url = releases_url.Replace("{/id}", "");

			dynamic dataReleases = ApiFetch(releases_url);

			int releaseCount = ((JArray)dataReleases).Count;

			if (releaseCount > 0)
			{
				string latest_url = DataRepo.releases_url;
				latest_url = latest_url.Replace("{/id}", "/latest");

				dynamic dataLatest = ApiFetch(latest_url);

				tag_name = (string)dataLatest.tag_name;
				published_at = (DateTime)dataLatest.published_at;

				foreach (dynamic asset in dataLatest.assets)
				{
					string name = (string)asset.name;
					string browser_download_url = (string)asset.browser_download_url;

					Assets.Add(name, browser_download_url);
				}
			}
		}

		public string Fetch(string url)
		{
			return Tools.FetchTextCached(url);
		}

		public dynamic ApiFetch(string url)
		{
			string json = Tools.FetchTextCached(url);

			if (json == null)
				return null;

			return JsonConvert.DeserializeObject<dynamic>(json);
		}
	}
}
