﻿using System;
using System.Collections.Generic;
using System.Linq;
using Abot.Poco;
using System.Net;

namespace Abot.Core
{
    /// <summary>
    /// Determines what pages should be crawled, whether the raw content should be downloaded and if the links on a page should be crawled
    /// </summary>
    public interface ICrawlDecisionMaker
    {
        /// <summary>
        /// Decides whether the page should be crawled
        /// </summary>
        CrawlDecision ShouldCrawlPage(PageToCrawl pageToCrawl, CrawlContext crawlContext);

        /// <summary>
        /// Decides whether the page's links should be crawled
        /// </summary>
        CrawlDecision ShouldCrawlPageLinks(CrawledPage crawledPage, CrawlContext crawlContext);

        /// <summary>
        /// Decides whether the page's content should be dowloaded
        /// </summary>
        CrawlDecision ShouldDownloadPageContent(CrawledPage crawledPage, CrawlContext crawlContext);

        /// <summary>
        /// Decides whether the page should be re-crawled
        /// </summary>
        CrawlDecision ShouldRecrawlPage(CrawledPage crawledPage, CrawlContext crawlContext);
    }

    public class CrawlDecisionMaker : ICrawlDecisionMaker
    {
        public virtual CrawlDecision ShouldCrawlPage(PageToCrawl pageToCrawl, CrawlContext crawlContext)
        {
            if(pageToCrawl == null)
                return new CrawlDecision { Allow = false, Reason = "Null page to crawl" };

            if (crawlContext == null)
                return new CrawlDecision { Allow = false, Reason = "Null crawl context" };

            if (pageToCrawl.RedirectedFrom != null && pageToCrawl.RedirectPosition > crawlContext.CrawlConfiguration.HttpRequestMaxAutoRedirects)
                return new CrawlDecision { Allow = false, Reason = $"HttpRequestMaxAutoRedirects limit of [{crawlContext.CrawlConfiguration.HttpRequestMaxAutoRedirects}] has been reached" };

            if(pageToCrawl.CrawlDepth > crawlContext.CrawlConfiguration.MaxCrawlDepth)
                return new CrawlDecision { Allow = false, Reason = "Crawl depth is above max" };

            if (!pageToCrawl.Uri.Scheme.StartsWith("http"))
                return new CrawlDecision { Allow = false, Reason = "Scheme does not begin with http" };

            //TODO Do we want to ignore redirect chains (ie.. do not treat them as seperate page crawls)?
            if (!pageToCrawl.IsRetry &&
                crawlContext.CrawlConfiguration.MaxPagesToCrawl > 0 &&
                crawlContext.CrawledCount + crawlContext.Scheduler.Count + 1 > crawlContext.CrawlConfiguration.MaxPagesToCrawl)
            {
                return new CrawlDecision { Allow = false, Reason = $"MaxPagesToCrawl limit of [{crawlContext.CrawlConfiguration.MaxPagesToCrawl}] has been reached" };
            }

            var pagesCrawledInThisDomain = 0;
            if (!pageToCrawl.IsRetry &&
                crawlContext.CrawlConfiguration.MaxPagesToCrawlPerDomain > 0 &&
                crawlContext.CrawlCountByDomain.TryGetValue(pageToCrawl.Uri.Authority, out pagesCrawledInThisDomain) &&
                pagesCrawledInThisDomain > 0)
            {
                if (pagesCrawledInThisDomain >= crawlContext.CrawlConfiguration.MaxPagesToCrawlPerDomain)
                    return new CrawlDecision { Allow = false, Reason = $"MaxPagesToCrawlPerDomain limit of [{crawlContext.CrawlConfiguration.MaxPagesToCrawlPerDomain}] has been reached for domain [{pageToCrawl.Uri.Authority}]" };
            }

            if(!crawlContext.CrawlConfiguration.IsExternalPageCrawlingEnabled && !pageToCrawl.IsInternal)
                return new CrawlDecision { Allow = false, Reason = "Link is external" };

            return new CrawlDecision { Allow = true };
        }

        public virtual CrawlDecision ShouldCrawlPageLinks(CrawledPage crawledPage, CrawlContext crawlContext)
        {
            if (crawledPage == null)
                return new CrawlDecision{Allow = false, Reason = "Null crawled page"};

            if (crawlContext == null)
                return new CrawlDecision { Allow = false, Reason = "Null crawl context" };

            if(string.IsNullOrWhiteSpace(crawledPage.Content.Text))
                return new CrawlDecision { Allow = false, Reason = "Page has no content" };

            if (!crawlContext.CrawlConfiguration.IsExternalPageLinksCrawlingEnabled && !crawledPage.IsInternal)
                return new CrawlDecision { Allow = false, Reason = "Link is external" };

            if (crawledPage.CrawlDepth >= crawlContext.CrawlConfiguration.MaxCrawlDepth)
                return new CrawlDecision { Allow = false, Reason = "Crawl depth is above max" };

            return new CrawlDecision{Allow = true};
        }

        public virtual CrawlDecision ShouldDownloadPageContent(CrawledPage crawledPage, CrawlContext crawlContext)
        {
            if (crawledPage == null)
                return new CrawlDecision { Allow = false, Reason = "Null crawled page" };

            if (crawlContext == null)
                return new CrawlDecision { Allow = false, Reason = "Null crawl context" };            

            if (crawledPage.HttpWebResponse == null)
                return new CrawlDecision { Allow = false, Reason = "Null HttpWebResponse" };
            
            if (crawledPage.HttpWebResponse.StatusCode != HttpStatusCode.OK)
                return new CrawlDecision { Allow = false, Reason = "HttpStatusCode is not 200" };
            
            var pageContentType = crawledPage.HttpWebResponse.ContentType.ToLower().Trim();
            var isDownloadable = false;
            var cleanDownloadableContentTypes = crawlContext.CrawlConfiguration.DownloadableContentTypes
                .Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            foreach (var downloadableContentType in cleanDownloadableContentTypes)
            {
                if (pageContentType.Contains(downloadableContentType.ToLower().Trim()))
                {
                    isDownloadable = true;
                    break;
                }
            }
            if (!isDownloadable)
                return new CrawlDecision { Allow = false, Reason = "Content type is not any of the following: " + string.Join(",", cleanDownloadableContentTypes) };

            if (crawlContext.CrawlConfiguration.MaxPageSizeInBytes > 0 && crawledPage.HttpWebResponse.ContentLength > crawlContext.CrawlConfiguration.MaxPageSizeInBytes)
                return new CrawlDecision { Allow = false, Reason = $"Page size of [{crawledPage.HttpWebResponse.ContentLength}] bytes is above the max allowable of [{crawlContext.CrawlConfiguration.MaxPageSizeInBytes}] bytes" };

            return new CrawlDecision { Allow = true };            
        }

        public virtual CrawlDecision ShouldRecrawlPage(CrawledPage crawledPage, CrawlContext crawlContext)
        {
            if (crawledPage == null)
                return new CrawlDecision { Allow = false, Reason = "Null crawled page" };

            if (crawlContext == null)
                return new CrawlDecision { Allow = false, Reason = "Null crawl context" };

            if (crawledPage.WebException == null)
                return new CrawlDecision { Allow = false, Reason = "WebException did not occur"};
           
            if (crawlContext.CrawlConfiguration.MaxRetryCount < 1)
                return new CrawlDecision { Allow = false, Reason = "MaxRetryCount is less than 1"};

            if (crawledPage.RetryCount >= crawlContext.CrawlConfiguration.MaxRetryCount)
                return new CrawlDecision {Allow = false, Reason = "MaxRetryCount has been reached"};

            return new CrawlDecision { Allow = true };
        }
    }
}
