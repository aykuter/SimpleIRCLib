﻿using System;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace SimpleIRCLib
{
    public class IrcConnect
    {
        //main connecting details
        public int newPort;
        public string newUsername;
        public string newPassword;
        public string newIP;
        public string newChannel;

        //global var to check if connection is establised
        public bool isConnectionEstablised { get; set; }

        //for dcc downloader, information about dcc
        public string packNumber { get; set; }
        public string bot { get; set; }

        //all seperate classes initialized
        private DCCClient dcc { get; set; }
        private SimpleIRC simpleirc { get; set; }

        //connection 
        private StreamReader reader;
        private NetworkStream stream;
        private TcpClient irc;
        private static StreamWriter writer;

        //async task for receiving messages from the server
        private Task receiverTask = null;

        private bool stopTask = false;

        //for userlist
        public List<String> Users = new List<String>();

        //Overload Constructor - safe way to get variables
        public IrcConnect(string IP, int Port, string Username, string Password, string Channel, SimpleIRC sirc)
        {
            newIP = IP;
            newPort = Port;
            newUsername = Username;
            newPassword = Password;
            newChannel = Channel;
            simpleirc = sirc;
            isConnectionEstablised = false;
            dcc = new DCCClient(simpleirc, this);
            Users = new List<String>();
        }

        //connects to irc server, gives a boolean back on succesfull connect etc
        public bool Connect()
        {
            try
            {
                irc = new TcpClient(newIP, newPort);
                stream = irc.GetStream();

                reader = new StreamReader(stream);
                writer = new StreamWriter(stream);

                receiverTask = new Task(StartReceivingChat);
                receiverTask.Start();
                receiverTask.Wait();
                if(newPassword.Length > 0)
                {
                    writeIrc("PASS " + newPassword);
                    Thread.Sleep(100);
                }

                writeIrc("USER " + newUsername + " 8 * :" + newUsername + "_SimpleIRCLib");
                Thread.Sleep(500);
                writeIrc("NICK " + newUsername);
                Thread.Sleep(500);
                writeIrc("JOIN 0");
                Thread.Sleep(500);
                writeIrc("JOIN " + newChannel);
                Thread.Sleep(500);
                simpleirc.DebugCallBack("succesful connected to the irc server!");
                return true;
            }
            catch(Exception e)
            {
                simpleirc.DebugCallBack("Error Connecting to IRC Server: \n " + e.ToString());
                return false;
            }
        }


        public void quitConnect()
        {
            simpleirc.DebugCallBack("\n STARTING QUITING PROCEDURE ");

            //send quit to server
            writeIrc("QUIT");

            simpleirc.shouldClientStop = true;
            stopTask = true;
            simpleirc.DebugCallBack("\n WRITTEN QUIT ");
            Thread.Sleep(200);
            //stop everything in right order
            receiverTask.Dispose();
            reader.Dispose();
            stream.Close();
            writer.Close();
            irc.Close();

            simpleirc.DebugCallBack("\n CLOSED EVERY THING ");
            isConnectionEstablised = false;

        }

        public void StartReceivingChat()
        {
            Task.Run(() => ReceiveChat());
        }

        private void ReceiveChat()
        {
            string ircData;
            try {
                while ((ircData = reader.ReadLine()) != null && !simpleirc.shouldClientStop && !stopTask)
                {
                    string userName;
                    isConnectionEstablised = true;
                    try
                    {
                        simpleirc.rawOutput(ircData);
                    }
                    catch
                    {

                    }

                    if (ircData.Contains("PING"))
                    {
                        string pingID = ircData.Split(':')[1];
                        simpleirc.DebugCallBack("RECEIVED PING WITH ID: " + pingID + "\n");
                        writeIrc("PONG :" + pingID);

                    }
                    if (!ircData.ToLower().Contains("callerid") && !ircData.ToLower().Contains("chantypes") && !ircData.ToLower().Contains("version"))
                    {

                        

                        string[] messageSplitFromData = ircData.Split(new string[] { newChannel }, StringSplitOptions.None);
                        string message = messageSplitFromData[messageSplitFromData.Length - 1];
                        try
                        {
                            message = message.Split(new string[] { newUsername + " :" }, StringSplitOptions.None)[1];
                        }
                        catch
                        {

                        }

                        string user = messageSplitFromData[0].Split('!')[0].Substring(1);

                        simpleirc.chatOutput(user, message);

                    }
                    else if (ircData.Contains("JOIN"))
                    {

                        Regex regex1 = new Regex(@"(?<user>(?<=:)(.*\n?)(?=!~))");
                        Match matches1 = regex1.Match(ircData);

                        if (matches1.Success)
                        {
                            userName = matches1.Value;
                            simpleirc.chatOutput(userName, "JOINED");
                        }

                    }
                    else if (ircData.Contains("QUIT"))
                    {
                        Regex regex1 = new Regex(@"(?<user>(?<=:)(.*\n?)(?=!~))");
                        Match matches1 = regex1.Match(ircData);

                        if (matches1.Success)
                        {
                            userName = matches1.Value;
                            simpleirc.chatOutput(userName, "QUITED");
                        }
                    }

                    if (ircData.Contains("DCC SEND") && ircData.Contains(newUsername))
                    {
                        dcc.startDownloader(ircData, simpleirc.downloadDir, bot, packNumber);


                        simpleirc.DebugCallBack("\n DCC SERVER REPLY: " + dcc.newDccString);
                        simpleirc.DebugCallBack("\n FILENAME: " + dcc.newFileName);
                        simpleirc.DebugCallBack("\n FILESIZE: " + dcc.newFileSize);
                        simpleirc.DebugCallBack("\n IP: " + dcc.newIp);
                        simpleirc.DebugCallBack("\n PORT: " + dcc.newPortNum);
                        simpleirc.DebugCallBack("\n PACK: " + dcc.packNum);
                        simpleirc.DebugCallBack("\n BOT: " + dcc.botName);
                    }

                    //RareIRC_Client = #weebirc :RareIRC_Client
                    if (ircData.Contains(newUsername + " = #"))
                    {
                        try
                        {
                            string userListFullString = ircData.Split(new[] { " = " }, StringSplitOptions.None)[1].Substring(newChannel.Length + 2);
                            string[] users = userListFullString.Split(' ');
                            foreach (string user in users)
                            {
                                Users.Add(user);
                            }
                        } catch (Exception e)
                        {
                            simpleirc.DebugCallBack("SOMETHING HAPPEND WITH USERNAME PARSING: " + e.ToString());
                        }
                                     
                        
                    }

                    if(ircData.ToLower().Contains("end of /names list"))
                    {
                        try
                        {

                            string[] userarray = Users.ToArray<String>();
                            simpleirc.UsersListReceived(userarray);
                        } catch
                        {

                        }
                    }
                    Thread.Sleep(1);
                }
                simpleirc.DebugCallBack("\n STOPPED RECEIVER: ");

                quitConnect();
                stopTask = false;
            } catch (Exception ioex)
            {
                simpleirc.DebugCallBack("ERROR: LOST CONNECTION TO SERVER PROBABLY. \n" + ioex.ToString() + "\n");
                if (isConnectionEstablised)
                {
                    stopTask = false;
                    quitConnect();
                }
            }
        }   

        public object[] passDownloadDetails()
        {
            if (dcc.isDownloading)
            {
                object[] downloadDetails = new object[12];

                downloadDetails[0] = dcc.newDccString;
                downloadDetails[1] = dcc.newFileName;
                downloadDetails[2] = dcc.newFileSize;
                downloadDetails[3] = dcc.newIp;
                downloadDetails[4] = dcc.newPortNum;
                downloadDetails[5] = dcc.packNum;
                downloadDetails[6] = dcc.botName;
                downloadDetails[7] = dcc.Bytes_Seconds;
                downloadDetails[8] = dcc.KBytes_Seconds;
                downloadDetails[9] = dcc.MBytes_Seconds;
                downloadDetails[10] = dcc.Progress;
                downloadDetails[11] = dcc.Status;
                return downloadDetails;
            } else
            {
                object[] downloadDetails = new object[12];

                downloadDetails[0] = "NO DOWNLOAD";
                downloadDetails[1] = "NO DOWNLOAD";
                downloadDetails[2] = 0;
                downloadDetails[3] = "0.0.0.0";
                downloadDetails[4] = 0;
                downloadDetails[5] = "#0";
                downloadDetails[6] = "NO DOWNLOAD";
                downloadDetails[7] = 0;
                downloadDetails[8] = 0;
                downloadDetails[9] = 0;
                downloadDetails[10] = 0;
                downloadDetails[11] = dcc.Status;
                return downloadDetails;
            }
            
        }

        // parse message to send
        public void sendMsg(string Input)
        {

            Input = Input.Trim();
            Regex regex1 = new Regex(@"^(?=.*(?<botname>(?<=/msg)(.*)(?=xdcc)))(?=.*(?<packnum>(?<=(send)+(\s))(.*)))");
            Match matches1 = regex1.Match(Input.ToLower());
            Regex regex2 = new Regex(@"^(?=.*(?<botname>(?<=/msg)(.*)(?=xdcc)))(?=.*(?<packnum>(?<=(cancel))(.*)))");
            Match matches2 = regex2.Match(Input.ToLower());
            Regex regex3 = new Regex(@"^(?=.*(?<botname>(?<=/msg)(.*)(?=xdcc)))(?=.*(?<packnum>(?<=(remove)+(\s))(.*)))");
            Match matches3 = regex3.Match(Input.ToLower());



            if (matches1.Success)
            {
                bot = matches1.Groups["botname"].Value.Trim(); 
                packNumber = matches1.Groups["packnum"].Value.Trim(); 
                string xdccdl = "PRIVMSG " + bot + " :XDCC SEND " + packNumber;
                simpleirc.DebugCallBack("XDCC FOUND: " + xdccdl + "\n");
                writeIrc(xdccdl);
            }
            else if (matches2.Success)
            {
                bot = matches2.Groups["botname"].Value;
                string xdcccl = "PRIVMSG " + bot + " :XDCC CANCEL";
                simpleirc.DebugCallBack("XDCC CANCELED");
                writeIrc(xdcccl);
            }
            else if (matches3.Success)
            {
                bot = matches3.Groups["botname"].Value;
                packNumber = matches3.Groups["packnum"].Value;
                string xdccdl = "PRIVMSG " + bot + " :XDCC REMOVE " + packNumber;
                simpleirc.DebugCallBack("XDCC REMOVED");
                writeIrc(xdccdl);
            } else if (Input.ToLower().Contains("/names"))
            {
                simpleirc.DebugCallBack("Requested nicknames on channel(s): " + Input);
                if (Input.Contains("#"))
                {
                    string channelList = Input.Split(' ')[1];
                    getUsersInChannel(channelList);
                } else
                {
                    getUsersInChannel("");
                }
            }
            else if(Input.ToLower().Contains("/quit"))
            {
                simpleirc.DebugCallBack("STARTED QUITTING");
                quitConnect();
            }
            else if (Input.ToLower().Contains("/msg"))
            {
                writeIrc(Input.Replace("/msg", "PRIVMSG"));
            }
            else
            {
                writeIrc("PRIVMSG " + newChannel + " :" + Input);
            }

            simpleirc.chatOutput(newUsername,  Input);
        }

        public void sendRawMsg(string msg)
        {
            writeIrc(msg);
        }

        //asks the server for all (visible) users in channel x (send empty string if you want over all channels on server)
        public void getUsersInChannel(string channel)
        {

            Users = new List<String>();
            if (channel != "")
            {
                writeIrc("NAMES " + channel);
                simpleirc.DebugCallBack("Asking for nicknames in channel: " + channel);
            } else
            {
                writeIrc("NAMES " + newChannel);
                simpleirc.DebugCallBack("Asking for nicknames in channel: " + channel);
            }
        }

        //function to write to the irc server, bit easier to use and better looking
        public void writeIrc(string input)
        {
            try
            {
                if (writer.BaseStream != null)
                {
                    writer.Write(input + Environment.NewLine);
                    writer.Flush();
                }
                else
                {
                    simpleirc.DebugCallBack("Could not send message" + input + ", irc client is not running :X, error: \n");
                }
            } catch(Exception e)
            {
                simpleirc.DebugCallBack("Could not send message" + input + ", irc client is not running :X, error: \n");
                simpleirc.DebugCallBack(e.ToString());
            }
           
        }

        //function for stopping the dcc downloader
        public bool stopXDCCDownload()
        {
            try
            {
                dcc.abortDownloader();
                return true;
            } catch (Exception e)
            {
                simpleirc.DebugCallBack("You probably are not downloading...");
                simpleirc.DebugCallBack(e.ToString());
                return false;
            }
        }
        //function for stopping the dcc downloader
        public bool checkIfDownloading()
        {
            try
            {
                return dcc.checkIfDownloading();
            }
            catch (Exception e)
            {
                simpleirc.DebugCallBack("You probably are not downloading...");
                simpleirc.DebugCallBack(e.ToString());
                return false;
            }
        }
    }

    
}
