using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Security.Cryptography;

namespace Downloader
{
    class ParallelDownloader
    {
        static void Main(string[] args)
        {
            Uri toDownload = new Uri("https://storage.googleapis.com/vimeo-test/work-at-vimeo-2.mp4");
            String writeTo = @"C:\Users\blian\Desktop\temp.mov";
            uint chunks = 4;
            String chunkFolderPath = @"C:\Users\blian\Desktop\";
            DownloadFile(toDownload, writeTo, chunks, chunkFolderPath);
        }

        static void DownloadFile(Uri toDownload, String writeTo, uint numberChunks, String chunkFolderPath)
        {
            //Cap on chunks set arbitrarily to 20
            uint maxChunks = 20;
            if (maxChunks > numberChunks)
            {
                Console.WriteLine("Cannot download " + numberChunks + " in parallel. Downloading the maximum: " + maxChunks);
                numberChunks = maxChunks;
            }
            ulong fileSize = 0;
            bool supportsRanged = true;

            //Single HTTP request to check if server accepts range requests, and to get content-length
            WebRequest request = WebRequest.Create(toDownload);
            request.Method = "HEAD";
            using (WebResponse response = request.GetResponse())
            {
                if (response.Headers.Get("Content-Length") != null)
                    fileSize = (ulong)int.Parse(response.Headers.Get("Content-Length"));

                if (response.Headers.Get("Accept-Ranges") == null)
                    supportsRanged = false;
            }
            if (supportsRanged && fileSize != 0)
                DownloadInParallel(toDownload, writeTo, numberChunks, fileSize, chunkFolderPath);
            else
                DownloadSingleStream(toDownload, writeTo, fileSize);
        }

        /// <summary>
        /// Donwload through continuously through a single threaded HTTP request.
        /// </summary>
        static void DownloadSingleStream(Uri toDownload, String writeTo, ulong fileSize)
        {
            bool continueTrying = true;
            int attempts = 0;
            int maxAttempts = 2;
            //Continue attempting to download if checksum is incorrect, with a two attempt limit
            while (continueTrying)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(toDownload);

                    using (WebResponse response = request.GetResponse())
                    using (Stream responseStream = response.GetResponseStream())
                    using (FileStream fileStream = new FileStream(writeTo, FileMode.OpenOrCreate))
                    using (MD5 md5 = MD5.Create())
                    {
                        responseStream.CopyTo(fileStream);

                        bool worked = md5.ComputeHash(responseStream).SequenceEqual(md5.ComputeHash(fileStream));
                        if (!worked)
                        {
                            attempts++;
                            if (attempts >= maxAttempts)
                                continueTrying = false;
                            throw new OperationCanceledException();
                        }
                    }
                }
                catch (WebException webException)
                {
                    Console.WriteLine(webException.Message);
                    Console.WriteLine("SingleStream retry");
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                    Console.WriteLine("SingleStream retry");
                }
            }
            if (attempts >= maxAttempts)
                Console.WriteLine("SingleStream Download failed");
            FileInfo tempInfo = new FileInfo(writeTo);
            if (tempInfo.Length == (long)fileSize)
                Console.WriteLine("Download succeeded");
        }

        /// <summary>
        /// Donwload in parallel through HTTP Range requests.
        /// </summary>
        static void DownloadInParallel(Uri toDownload, String writeTo, uint numberChunks, ulong fileSize, String chunkFolderPath)
        {
            ulong chunkSize = fileSize / numberChunks;

            //Start a new task for every chunk
            List<Task<String>> downloadList = new List<Task<String>>();
            for (uint i = 0; i < numberChunks; i++)
            {
                var start = i * chunkSize;
                var end = start + chunkSize - 1;
                if (i == numberChunks - 1)
                    end = start + chunkSize + fileSize % numberChunks - 1;
                downloadList.Add(DownloadChunk(toDownload, start, end, i, chunkFolderPath));
            }

            Task.WaitAll(downloadList.ToArray());
            try
            {
                using (FileStream output = File.Create(writeTo))
                {
                    foreach (var file in downloadList)
                    {
                        using (var input = File.OpenRead(file.Result))
                        {
                            //Concatenate chunks if they were successfully downloaded
                            if (file.Result == "Download Chunk failed")
                                throw new MissingFieldException();
                            input.CopyTo(output);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                Console.WriteLine("Parallel Download failed");
            }
            FileInfo tempInfo = new FileInfo(writeTo);
            if (tempInfo.Length == (long)fileSize)
                Console.WriteLine("Download succeeded");
        }

        /// <summary>
        /// Donwload a chunk via an HTTP Range request.
        /// </summary>
        static async Task<String> DownloadChunk(Uri toDownload, ulong start, ulong end, uint chunkIndex, String chunkFolderPath)
        {
            String chunkPath = chunkFolderPath + "part_" + chunkIndex + ".mp4";
            bool continueTrying = true;
            int attempts = 0;
            while (continueTrying)
            {
                try
                {
                    //HTTP Range request with start and end range specified by DownloadInParallel loop
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(toDownload);
                    request.AddRange((long)start, (long)end);

                    using (WebResponse response = await request.GetResponseAsync())
                    using (Stream responseStream = response.GetResponseStream())
                    using (FileStream fileStream = new FileStream(chunkPath, FileMode.OpenOrCreate))
                    using (MD5 md5 = MD5.Create())
                    {
                        await responseStream.CopyToAsync(fileStream);

                        bool worked = md5.ComputeHash(responseStream).SequenceEqual(md5.ComputeHash(fileStream));
                        if (!worked)
                        {
                            attempts++;
                            if (attempts >= 3)
                                continueTrying = false;
                            throw new OperationCanceledException();
                        }
                        else
                            continueTrying = false;
                        Console.WriteLine("Part " + chunkIndex + " finished downloading!");
                        return chunkPath;
                    }
                }
                catch (WebException webException)
                {
                    Console.WriteLine(webException.Message);
                    Console.WriteLine("Download Chunk " + chunkIndex + "failed");
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                    Console.WriteLine("Download Chunk " + chunkIndex + "failed");
                }
            }
            return "Download Chunk failed";
        }
    }
}