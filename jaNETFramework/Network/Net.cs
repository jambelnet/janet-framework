/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
 *
 * License:
 *  This file is part of jaNET Framework.

    jaNET Framework is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    jaNET Framework is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */

using jaNET.Diagnostics;
using jaNET.Environment;
using jaNET.Extensions;
using jaNET.IO;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
//using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace jaNET.Net
{
    static class NetInfo
    {
        internal class DynDns
        {
            internal string Hostname { get; set; }
            internal string Username { get; set; }
            internal string Password { get; set; }

            internal DynDns() {
                var ddnsSettings = new Settings().Load(".dyndnssettings");

                if (ddnsSettings != null) {
                    Hostname = ddnsSettings[0];
                    Username = ddnsSettings[1];
                    Password = ddnsSettings[2];
                }
            }

            internal static async Task<string> CheckIpAsync() {
                return Regex.Match(await Helpers.Http.GetAsync("http://checkip.dyndns.org"), @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b").Value;
            }

            internal static async void DynamicUpdateAsync(DynDns ddns) {
                try {
                    string noIpUri = string.Format("http://dynupdate.no-ip.com/nic/update?hostname={0}&myip={1}", ddns.Hostname, CheckIpAsync().Result);

                    using (var client = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential(ddns.Username, ddns.Password) }))
                    using (var response = await client.GetAsync(noIpUri))
                    using (var content = response.Content)
                        await content.ReadAsStringAsync();
                }
                catch (Exception e) {
                    Logger.Instance.Append(string.Format("obj [ DynDns.DynamicUpdate <Exception> ] Exception Message: [ {0} ]", e.Message));
                }
            }
        }

        internal static class SimplePing
        {
            static Boolean resolveHostEntry(string entry) {
                return new Regex("[^a-zA-Z]").IsMatch(entry);
            }

            internal static bool Ping(string host = null, int timeout = 1000) {
                return Pinger(host, timeout);
            }

            // Original post: http://www.java2s.com/Code/CSharp/Network/SimplePing.htm
            // Modified by jambel
            // TCP Ping
            // Original post: http://stackoverflow.com/questions/26067342/how-to-implement-psping-tcp-ping-in-c-sharp
            static bool altPing(string endPoint) {
                try {
                    IPHostEntry hostEntry;
                    IPAddress hostAddress;
                    IPEndPoint iep;

                    if (resolveHostEntry(endPoint)) {
                        hostEntry = Dns.GetHostEntry(endPoint);
                        hostAddress = hostEntry.AddressList[0];
                        iep = new IPEndPoint(hostAddress, 0);
                    }
                    else
                        iep = new IPEndPoint(IPAddress.Parse(endPoint), 0);

                    var data = new byte[1024];
                    var host = new Socket(iep.AddressFamily, SocketType.Raw, ProtocolType.Icmp);
                    EndPoint ep = iep;
                    var packet = new ICMP();

                    packet.Type = 0x08;
                    packet.Code = 0x00;
                    packet.Checksum = 0;
                    Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, packet.Message, 0, 2);
                    Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, packet.Message, 2, 2);
                    data = Encoding.ASCII.GetBytes("test packet");
                    Buffer.BlockCopy(data, 0, packet.Message, 0, data.Length);
                    packet.MessageSize = data.Length + 4;
                    int packetsize = packet.MessageSize + 4;

                    UInt16 chcksum = packet.GetChecksum();
                    packet.Checksum = chcksum;

                    host.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 3000);
                    host.SendTo(packet.GetBytes(), packetsize, SocketFlags.None, iep);

                    data = new byte[1024];
                    int recv = host.ReceiveFrom(data, ref ep);

                    /*ICMP response = new ICMP(data, recv);
                    Console.WriteLine("response from: {0}", ep.ToString());
                    Console.WriteLine("  Type {0}", response.Type);
                    Console.WriteLine("  Code: {0}", response.Code);
                    int Identifier = BitConverter.ToInt16(response.Message, 0);
                    int Sequence = BitConverter.ToInt16(response.Message, 2);
                    Console.WriteLine("  Identifier: {0}", Identifier);
                    Console.WriteLine("  Sequence: {0}", Sequence);
                    string stringData = Encoding.ASCII.GetString(response.Message, 0, response.MessageSize);
                    Console.WriteLine("  data: {0}", stringData);*/

                    host.Close();
                    return true;
                }
                catch (SocketException) {
                    return false;
                }
            }

            static bool Pinger(string host, int timeout) {
                try {
                    bool network_available = NetworkInterface.GetIsNetworkAvailable();

                    if (network_available) {
                        using (var pingSender = new Ping()) {
                            // Create a buffer of 32 bytes of data to be transmitted.
                            const string data = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                            byte[] buffer = Encoding.ASCII.GetBytes(data);

                            PingReply reply = pingSender.Send(host, timeout, buffer);

                            return (reply.Status == IPStatus.Success);
                        }
                    }
                    return false;
                }
                catch (Exception e) {
                    Logger.Instance.Append(string.Format("obj [ NetInfo.SimplePing.Pinger <Exception> ] Exception Message: [ {0} ]", e.Message));
                    return false;
                }
            }
        }

        class ICMP
        {
            internal byte Type;
            internal byte Code;
            internal UInt16 Checksum;
            internal int MessageSize;
            internal byte[] Message = new byte[1024];

            public ICMP() {

            }

            public ICMP(byte[] data, int size) {
                Type = data[20];
                Code = data[21];
                Checksum = BitConverter.ToUInt16(data, 22);
                MessageSize = size - 24;
                MessageSize = (MessageSize < 0) ? 0 : MessageSize;
                Buffer.BlockCopy(data, 24, Message, 0, MessageSize);
            }

            public byte[] GetBytes() {
                var data = new byte[MessageSize + 9];
                Buffer.BlockCopy(BitConverter.GetBytes(Type), 0, data, 0, 1);
                Buffer.BlockCopy(BitConverter.GetBytes(Code), 0, data, 1, 1);
                Buffer.BlockCopy(BitConverter.GetBytes(Checksum), 0, data, 2, 2);
                Buffer.BlockCopy(Message, 0, data, 4, MessageSize);
                return data;
            }

            public UInt16 GetChecksum() {
                UInt32 chcksm = 0;
                byte[] data = GetBytes();
                int packetsize = MessageSize + 8;
                int index = 0;

                while (index < packetsize) {
                    chcksm += Convert.ToUInt32(BitConverter.ToUInt16(data, index));
                    index += 2;
                }
                chcksm = (chcksm >> 16) + (chcksm & 0xffff);
                chcksm += (chcksm >> 16);
                return (UInt16)(~chcksm);
            }
        }
    }

    class Mail
    {
        internal bool Send(string sFrom, string sTo, string sSubject, string sBody) {
            if (!Methods.Instance.HasInternetConnection())
                return false;

            try {
                var smtpSettings = new SmtpSettings();

                if (smtpSettings.Host != null) {
                    var mail = new MailMessage();

                    mail.From = new MailAddress(sFrom);
                    mail.To.Add(sTo);
                    mail.Subject = sSubject;
                    mail.Body = sBody;

                    var smtpClient = new SmtpClient(smtpSettings.Host);
                    smtpClient.Port = smtpSettings.Port;
                    smtpClient.Credentials = new NetworkCredential(smtpSettings.Username, smtpSettings.Password);
                    smtpClient.EnableSsl = smtpSettings.SSL;
                    if (smtpClient.EnableSsl)
                        ServicePointManager.ServerCertificateValidationCallback =
                            (s, certificate, chain, sslPolicyErrors) => true;
                    //(object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;
                    //delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; };
                    smtpClient.Send(mail);
                    return true;
                }
                return false; // Settings not found
            }
            catch {
                return false;
            }
        }

        internal int Pop3Check() {
            try {
                var pop3Settings = new Pop3Settings();
                var obj = new Pop3();
                obj.Connect(pop3Settings.Host, pop3Settings.Username, pop3Settings.Password, pop3Settings.Port);
                string KeyWord = Helpers.Xml.AppConfigQuery("jaNET/System/Comm/MailKeyword").Item(0).InnerText;

                foreach (Pop3Message msg in obj.List()) {
                    Pop3Message msg2 = obj.Retrieve(msg);
                    /*Console.WriteLine("Message {0}: {1}",
                        msg2.number, msg2.message);*/
                    if (msg2.Message.Contains("<" + KeyWord + ">")) {
                        //If a command found to mail subject
                        var Command = Regex.Match(msg2.Message.Replace("\r\n", " "), @"(<" + KeyWord + ">)(.*?)(?=</" + KeyWord + ">)");
                        Command.ToString().ToLower().Replace("<" + KeyWord + ">", string.Empty).Parse();
                        obj.Delete(msg2);
                    }
                    else {
                        // For Future Use
                        /*Match From = Regex.Match(msg2.message, @"(?<=From: )(.*?)(?= <)");
                        Match Subject = Regex.Match(msg2.message, @"(?<=Subject: )(.*?)(?=\\r\\nDate: )"); //(?<=Subject:</B> )(.*?)(?=</)");
                        MailList.Add("From " + From.ToString() + ", Subject " + Subject.ToString());*/
                        //From pattern (?<=From: \\\")(.*?)(?=\\\")
                        //Subject pattern (?<=Subject: )(.*?)(?=\\r)
                    }
                }
                obj.Disconnect();
                return obj.List().Count;
            }
            catch {
                return 0;
            }
        }

        internal string GmailCheck(bool countOnly) {
            try {
                // Change SSL checks so that all checks pass
                ServicePointManager.ServerCertificateValidationCallback =
                    new RemoteCertificateValidationCallback(
                        delegate { return true; }
                    );

                WebRequest webGmailRequest = WebRequest.Create(@"https://mail.google.com/mail/feed/atom");
                webGmailRequest.PreAuthenticate = true;

                var gmailSettings = new GmailSettings();
                string gmailUser = gmailSettings.Username;
                string gmailPassword = gmailSettings.Password;
                var loginCredentials = new NetworkCredential(gmailUser, gmailPassword);
                webGmailRequest.Credentials = loginCredentials;

                WebResponse webGmailResponse = webGmailRequest.GetResponse();
                Stream strmUnreadMailInfo = webGmailResponse.GetResponseStream();

                var sbUnreadMailInfo = new StringBuilder(); var buffer = new byte[8192]; int byteCount = 0;

                while ((byteCount = strmUnreadMailInfo.Read(buffer, 0, buffer.Length)) > 0)
                    sbUnreadMailInfo.Append(Encoding.ASCII.GetString(buffer, 0, byteCount));

                var UnreadMailXmlDoc = new XmlDocument();
                UnreadMailXmlDoc.LoadXml(sbUnreadMailInfo.ToString());
                XmlNodeList UnreadMailEntries = UnreadMailXmlDoc.GetElementsByTagName("entry");

                if (!countOnly) {
                    string output = string.Empty;
                    for (int i = 0; i < UnreadMailEntries.Count; ++i) {
                        output += string.Format("{0}\r\n", ("Message " + (i + 1)));
                        output += string.Format("{0}\r\n", ("Subject: " + (UnreadMailEntries[i]["title"]).InnerText));
                        output += string.Format("{0}\r\n", ("From: " + (UnreadMailEntries[i]["author"])["name"].InnerText +
                                    " <" + (UnreadMailEntries[i]["author"])["email"].InnerText + ">"));
                        output += string.Format("{0}\r\n", ("Date: " + DateTime.Parse((UnreadMailEntries[i]["modified"]).InnerText)));
                    }
                    output += "Total: " + UnreadMailEntries.Count;
                    return output;
                }
                return UnreadMailEntries.Count.ToString();
            }
            catch {
                return "0";
            }
        }

        public class Pop3Exception : ApplicationException
        {
            public Pop3Exception(string str)
                : base(str) {
            }
        }
        public class Pop3Message
        {
            public long Number;
            public long Bytes;
            public bool Retrieved;
            public string Message;
        }
        public class Pop3 : TcpClient
        {
            public void Connect(string server, string username, string password, int port) {
                try {
                    string message;
                    string response;

                    Connect(server, port);
                    response = Response();
                    if (response == string.Empty) {
                        throw new Pop3Exception(response);
                    }
                    if (response.Substring(0, 3) != "+OK") {
                        throw new Pop3Exception(response);
                    }

                    message = "USER " + username + "\r\n";
                    Write(message);
                    response = Response();
                    if (response.Substring(0, 3) != "+OK") {
                        throw new Pop3Exception(response);
                    }

                    message = "PASS " + password + "\r\n";
                    Write(message);
                    response = Response();
                    if (response.Substring(0, 3) != "+OK") {
                        throw new Pop3Exception(response);
                    }
                }
                catch (Exception e) {
                    Debug.Print(e.Message);
                }
            }
            public void Disconnect() {
                try {
                    string message;
                    string response;
                    message = "QUIT\r\n";
                    Write(message);
                    response = Response();
                    if (response.Substring(0, 3) != "+OK") {
                        throw new Pop3Exception(response);
                    }
                }
                catch (Exception e) {
                    Debug.Print(e.Message);
                }
            }
            public ArrayList List() {
                try {
                    string message;
                    string response;

                    var retval = new ArrayList();
                    message = "LIST\r\n";
                    Write(message);
                    response = Response();
                    if (response.Substring(0, 3) != "+OK") {
                        throw new Pop3Exception(response);
                    }

                    while (true) {
                        response = Response();
                        if (response == ".\r\n") {
                            return retval;
                        }
                        else {
                            var msg = new Pop3Message();
                            char[] seps = { ' ' };
                            string[] values = response.Split(seps);
                            msg.Number = Int32.Parse(values[0]);
                            msg.Bytes = Int32.Parse(values[1]);
                            msg.Retrieved = false;
                            retval.Add(msg);
                            continue;
                        }
                    }
                }
                catch (Exception e) {
                    Debug.Print(e.Message);
                    return null;
                }
            }
            public Pop3Message Retrieve(Pop3Message rhs) {
                try {
                    string message;
                    string response;

                    var msg = new Pop3Message();
                    msg.Bytes = rhs.Bytes;
                    msg.Number = rhs.Number;

                    message = "RETR " + rhs.Number + "\r\n";
                    Write(message);
                    response = Response();
                    if (response.Substring(0, 3) != "+OK") {
                        throw new Pop3Exception(response);
                    }

                    msg.Retrieved = true;
                    while (true) {
                        response = Response();
                        if (response == ".\r\n") {
                            break;
                        }
                        else {
                            msg.Message += response;
                        }
                    }
                    return msg;
                }
                catch (Exception e) {
                    Debug.Print(e.Message);
                    return null;
                }
            }
            public void Delete(Pop3Message rhs) {
                try {
                    string message;
                    string response;

                    message = "DELE " + rhs.Number + "\r\n";
                    Write(message);
                    response = Response();
                    if (response.Substring(0, 3) != "+OK") {
                        throw new Pop3Exception(response);
                    }
                }
                catch (Exception e) {
                    Debug.Print(e.Message);
                }
            }
            void Write(string message) {
                try {
                    var en = new ASCIIEncoding();

                    byte[] WriteBuffer = new byte[1024];
                    WriteBuffer = en.GetBytes(message);

                    NetworkStream stream = GetStream();
                    stream.Write(WriteBuffer, 0, WriteBuffer.Length);

                    //Debug.WriteLine("WRITE:" + message);
                }
                catch (Exception e) {
                    Debug.Print(e.Message);
                }
            }
            string Response() {
                try {
                    var enc = new ASCIIEncoding();
                    byte[] serverbuff = new Byte[1024];
                    NetworkStream stream = GetStream();
                    int count = 0;
                    while (true) {
                        byte[] buff = new Byte[2];
                        int bytes = stream.Read(buff, 0, 1);
                        if (bytes == 1) {
                            serverbuff[count] = buff[0];
                            count++;
                            if (buff[0] == '\n') {
                                break;
                            }
                        }
                        else {
                            break;
                        }
                    }
                    string retval = enc.GetString(serverbuff, 0, count);
                    //Debug.WriteLine("READ:" + retval);
                    return retval;
                }
                catch (Exception e) {
                    return e.Message;
                }
            }
        }

        internal class SmtpSettings
        {
            // Gmail settings "smtp.gmail.com"
            // Gmail port 587
            // Gmail SSL true

            internal string Host { get; private set; }
            internal string Username { get; private set; }
            internal string Password { get; private set; }
            internal int Port { get; private set; }
            internal bool SSL { get; private set; }

            internal SmtpSettings() {
                var smtpSettings = new Settings().Load(".smtpsettings");

                if (smtpSettings != null) {
                    Host = smtpSettings[0];
                    Username = smtpSettings[1];
                    Password = smtpSettings[2];
                    Port = Convert.ToInt32(smtpSettings[3]);
                    SSL = Convert.ToBoolean(smtpSettings[4]);
                }
            }
        }

        internal class Pop3Settings
        {
            internal string Host { get; private set; }
            internal string Username { get; private set; }
            internal string Password { get; private set; }
            internal int Port { get; private set; }
            internal bool SSL { get; private set; }

            internal Pop3Settings() {
                var pop3Settings = new Settings().Load(".pop3settings");

                if (pop3Settings != null) {
                    Host = pop3Settings[0];
                    Username = pop3Settings[1];
                    Password = pop3Settings[2];
                    Port = Convert.ToInt32(pop3Settings[3]);
                    SSL = Convert.ToBoolean(pop3Settings[4]);
                }
            }
        }

        internal class GmailSettings
        {
            internal string Username { get; private set; }
            internal string Password { get; private set; }

            internal GmailSettings() {
                var gmailSettings = new Settings().Load(".gmailsettings");

                if (gmailSettings != null) {
                    Username = gmailSettings[0];
                    Password = gmailSettings[1];
                }
            }
        }
    }

    #region SMS
    class SMS
    {
        internal string Username { get; private set; }
        internal string Password { get; private set; }
        internal string API { get; private set; }

        internal SMS() {
            var smsSettings = new Settings().Load(".smssettings");

            if (smsSettings != null) {
                API = smsSettings[0];
                Username = smsSettings[1];
                Password = smsSettings[2];
            }
        }

        internal string Send(string smsTo, string smsMsg) {
            var client = new WebClient();
            //var smsSettings = new SmsSettings();
            // Add a user agent header in case the requested URI contains a query.
            client.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
            client.QueryString.Add("user", Username);
            client.QueryString.Add("password", Password);
            client.QueryString.Add("api_id", API);
            client.QueryString.Add("to", smsTo);
            client.QueryString.Add("text", smsMsg);
            const string baseurl = "http://api.clickatell.com/http/sendmsg";
            //using (Stream data = client.OpenRead(baseurl))
            using (var reader = new StreamReader(client.OpenRead(baseurl))) // data
                return reader.ReadToEnd();
        }
    }
    #endregion
}