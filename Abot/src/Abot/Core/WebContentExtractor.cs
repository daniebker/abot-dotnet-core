﻿
using Abot.Poco;
using log4net;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Abot.Core
{
    public interface IWebContentExtractor : IDisposable
    {
        Task<PageContent> GetContent(HttpResponseMessage httpResponseMessage);
    }

    public class WebContentExtractor : IWebContentExtractor
    {
        static ILog _logger = LogManager.GetLogger("AbotLogger");

        public virtual async Task<PageContent> GetContent(HttpResponseMessage httpResponseMessage)
        {
            using (MemoryStream memoryStream = await GetRawData(httpResponseMessage))
            {
                String charset = GetCharsetFromHeaders(httpResponseMessage);

                if (charset == null) {
                    memoryStream.Seek(0, SeekOrigin.Begin);

                    // Do not wrap in closing statement to prevent closing of this stream.
                    StreamReader srr = new StreamReader(memoryStream, Encoding.ASCII);
                    String body = srr.ReadToEnd();
                    charset = GetCharsetFromBody(body);
                }
                memoryStream.Seek(0, SeekOrigin.Begin);

                charset = CleanCharset(charset);
                Encoding e = GetEncoding(charset);
                string content = "";
                using (StreamReader sr = new StreamReader(memoryStream, e))
                {
                    content = sr.ReadToEnd();
                }

                PageContent pageContent = new PageContent();
                pageContent.Bytes = memoryStream.ToArray();
                pageContent.Charset = charset;
                pageContent.Encoding = e;
                pageContent.Text = content;

                return pageContent;
            }
        }

        protected virtual string GetCharsetFromHeaders(HttpResponseMessage httpResponseMessage)
        {
            return httpResponseMessage.Content.Headers.ContentType.CharSet;
        }

        protected virtual string GetCharsetFromBody(string body)
        {
            String charset = null;
            
            if (body != null)
            {
                //find expression from : http://stackoverflow.com/questions/3458217/how-to-use-regular-expression-to-match-the-charset-string-in-html
                Match match = Regex.Match(body, @"<meta(?!\s*(?:name|value)\s*=)(?:[^>]*?content\s*=[\s""']*)?([^>]*?)[\s""';]*charset\s*=[\s""']*([^\s""'/>]*)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    charset = string.IsNullOrWhiteSpace(match.Groups[2].Value) ? null : match.Groups[2].Value;
                }
            }

            return charset;
        }
        
        protected virtual Encoding GetEncoding(string charset)
        {
            Encoding e = Encoding.UTF8;
            if (charset != null)
            {
                try
                {
                    e = Encoding.GetEncoding(charset);
                }
                catch{}
            }

            return e;
        }

        protected virtual string CleanCharset(string charset)
        {
            //TODO temporary hack, this needs to be a configurable value
            if (charset == "cp1251") //Russian, Bulgarian, Serbian cyrillic
                charset = "windows-1251";

            return charset;
        }

        private async Task<MemoryStream> GetRawData(HttpResponseMessage responseMessage)
        {
            MemoryStream rawData = new MemoryStream();

            try
            {
                using (Stream rs = await responseMessage.Content.ReadAsStreamAsync())
                {
                    byte[] buffer = new byte[1024];
                    int read = rs.Read(buffer, 0, buffer.Length);
                    while (read > 0)
                    {
                        rawData.Write(buffer, 0, read);
                        read = rs.Read(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.WarnFormat("Error occurred while downloading content of url {0}", responseMessage.RequestMessage.RequestUri);
                _logger.Warn(e);
            }

            return rawData;
        }

        public virtual void Dispose()
        {
            // Nothing to do
        }
    }
}