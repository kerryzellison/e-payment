using BNITapCash.Interface;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Text;
using BNITapCash.ConstantVariable;

namespace BNITapCash.API
{
    class RESTAPI : RestAPIMethod
    {
        private const int TIMEOUT_CONNECTION = 5000; // 3 seconds
        public RESTAPI()
        {

        }

        public DataResponse post(string ip_address_server, string APIUrl, bool resultSingleObject = false, string sent_param = "")
        {
            string result = "";
            try
            {
                string full_API_URL = Constant.URL_PROTOCOL + ip_address_server + Properties.Resources.repo + APIUrl;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(full_API_URL);
                request.Method = "POST";
                request.Timeout = TIMEOUT_CONNECTION;

                System.Text.UTF8Encoding encoding = new System.Text.UTF8Encoding();
                Byte[] byteArray = encoding.GetBytes(sent_param);

                request.ContentLength = byteArray.Length;
                request.ContentType = @"application/json";
                using (Stream dataStream = request.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                }
                long length = 0;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    length = response.ContentLength;
                    Stream receiveStream = response.GetResponseStream();
                    StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);
                    result = readStream.ReadToEnd();
                    DataResponse json = null;
                    if (resultSingleObject)
                    {
                        json = JsonConvert.DeserializeObject<DataResponseObject>(result);
                    }
                    else
                    {
                        json = JsonConvert.DeserializeObject<DataResponseArray>(result);
                    }
                    return json;
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public DataResponse get(string ip_address_server, string API_URL, bool resultSingleObject = false)
        {
            string result = "";
            try
            {
                string full_URL_API = Constant.URL_PROTOCOL + ip_address_server + Properties.Resources.repo + API_URL;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(full_URL_API);
                request.Method = "GET";
                request.Timeout = TIMEOUT_CONNECTION;
                WebResponse response = request.GetResponse();
                using (Stream responseStream = response.GetResponseStream())
                {
                    StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);
                    result = reader.ReadToEnd();
                    DataResponse json = null;
                    if (resultSingleObject)
                    {
                        json = JsonConvert.DeserializeObject<DataResponseObject>(result);
                    }
                    else
                    {
                        json = JsonConvert.DeserializeObject<DataResponseArray>(result);
                    }
                    return json;
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }

        }
    }
}
