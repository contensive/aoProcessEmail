
using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Pop3
{
	public class Pop3Client
	{
		private Pop3Credential m_credential;

		//private const int m_pop3port = 110;
		private const int MAX_BUFFER_READ_SIZE = (64 * 1024)-1;
		
		private long m_inboxPosition = 0;
		private long m_directPosition = -1;

		private Socket m_socket = null;
 
		private Pop3Message m_pop3Message = null;

		public Pop3Credential userDetails
		{
			set { m_credential = value; }
			get { return m_credential; }
		}

		public string from
		{
			get { return m_pop3Message.From; }
		}

		public string to
		{
			get { return m_pop3Message.To; }
		}

		public string subject
		{
			get { return m_pop3Message.Subject; }
		}

		public string body
		{
			get { return m_pop3Message.Body; }
		}

		public IEnumerator multipartEnumerator
		{
			get { return m_pop3Message.MultipartEnumerator; }
		}

		public bool isMultipart
		{
			get { return m_pop3Message.IsMultipart; }
		}


		public Pop3Client(string user, string pass, string server, int port)
		{
			m_credential = new Pop3Credential(user,pass,server, port );
		}

		private Socket GetClientSocket()
		{
			Socket returnSocket = null;
			
			try
			{
				IPHostEntry hostEntry = null;
        
				// Get host related information.
				hostEntry = Dns.GetHostEntry(m_credential.Server);

				// Loop through the AddressList to obtain the supported 
				// AddressFamily. This is to avoid an exception that 
				// occurs when the host IP Address is not compatible 
				// with the address family 
				// (typical in the IPv6 case).
				
				foreach(IPAddress address in hostEntry.AddressList)
				{
					IPEndPoint ipe = new IPEndPoint(address, m_credential.port );
				
					Socket tempSocket = 
						new Socket(ipe.AddressFamily, 
						SocketType.Stream, ProtocolType.Tcp);

					tempSocket.Connect(ipe);

					if(tempSocket.Connected)
					{
						// we have a connection.
						// return this socket ...
						returnSocket = tempSocket;
						break;
					}
					else
					{
						continue;
					}
				}
			}
			catch(Exception e)
			{
				throw new Pop3ConnectException(e.ToString());
			}

			// throw exception if can't connect ...
            if (returnSocket == null)
            {
                throw new Pop3ConnectException("Error : connecting to " + m_credential.Server);
            }
            else
            {
                //returnSocket.ExclusiveAddressUse = true;

                // The socket will linger for 10 seconds after 
                // Socket.Close is called.
                //returnSocket.LingerState = new LingerOption(true, 10);

                // Disable the Nagle Algorithm for this tcp socket.
                //returnSocket.NoDelay = true;

                // Set the receive buffer size to 8k
                returnSocket.ReceiveBufferSize = MAX_BUFFER_READ_SIZE;

                // Set the timeout for synchronous receive methods to 
                // 1 second (1000 milliseconds.)
                returnSocket.ReceiveTimeout = 120000;

                // Set the send buffer size to 8k.
                returnSocket.SendBufferSize = MAX_BUFFER_READ_SIZE;

                // Set the timeout for synchronous send methods
                // to 1 second (1000 milliseconds.)			
                returnSocket.SendTimeout = 120000;

                // Set the Time To Live (TTL) to 42 router hops.
                returnSocket.Ttl = 42;
            }
			
			return returnSocket;
		}

		//send the data to server
		private void Send(String data) 
		{
			if(m_socket == null)
			{
				throw new Pop3MessageException("Pop3 connection is closed");
			}

			try
			{
				// Convert the string data to byte data 
				// using ASCII encoding.
				
				byte[] byteData = Encoding.ASCII.GetBytes(data+"\r\n");
				
				// Begin sending the data to the remote device.
				m_socket.Send(byteData);
			}
			catch(Exception e)
			{
				throw new Pop3SendException(e.ToString());
			}
		}

		private string GetPop3String()
		{
			if(m_socket == null)
			{
				throw new 
					Pop3MessageException("Connection to POP3 server is closed");
			}

			byte[] buffer = new byte[MAX_BUFFER_READ_SIZE];
			string line = null;

			try
			{
				int byteCount = 
					m_socket.Receive(buffer,buffer.Length,0);

				line = 
					Encoding.ASCII.GetString(buffer, 0, byteCount);
			}
			catch(Exception e)
			{
				throw new Pop3ReceiveException(e.ToString());
			}

			return line;
		}

		private void LoginToInbox()
		{
			string returned;

			// send username ...
			Send("user "+m_credential.User);
		
			// get response ...
			returned = GetPop3String();

			if( !returned.Substring(0,3).Equals("+OK") )
			{
				throw new Pop3LoginException("login not excepted");
			}

			// send password ...
			Send("pass "+m_credential.Pass);

			// get response ...
			returned = GetPop3String();

			if( !returned.Substring(0,3).Equals("+OK") )
			{
				throw new 
					Pop3LoginException("login/password not accepted");
			}
		}

		public long messageCount
		{
			get 
			{
				long count = 0;
			
				if(m_socket==null)
				{
					throw new Pop3MessageException("Pop3 server not connected");
				}

				Send("stat");

				string returned = GetPop3String();

				// if values returned ...
				if( Regex.Match(returned,
					@"^.*\+OK[ |	]+([0-9]+)[ |	]+.*$").Success )
				{
						// get number of emails ...
						count = long.Parse( Regex
						.Replace(returned.Replace("\r\n","")
						, @"^.*\+OK[ |	]+([0-9]+)[ |	]+.*$" ,"$1") );
				}

				return(count);
			}
		}


		public void closeConnection()
		{			
			Send("quit");

			m_socket = null;
			m_pop3Message = null;
		}

		public bool deleteEmail()
		{
			bool ret = false;

			Send("dele "+m_inboxPosition);

			string returned = GetPop3String();

			if( Regex.Match(returned,
				@"^.*\+OK.*$").Success )
			{
				ret = true;
			}

			return ret;
		}

		public bool nextEmail(long directPosition)
		{
			bool ret;

			if( directPosition >= 0 )
			{
				m_directPosition = directPosition;
				ret = nextEmail();
			}
			else
			{
				throw new Pop3MessageException("Position less than zero");
			}

			return ret;
		}

		public bool nextEmail()
		{
			string returned;

			long pos;

			if(m_directPosition == -1)
			{
				if(m_inboxPosition == 0)
				{
					pos = 1;
				}
				else
				{
					pos = m_inboxPosition + 1;
				}
			}
			else
			{
				pos = m_directPosition+1;
				m_directPosition = -1;
			}

			// send username ...
			Send("list "+pos.ToString());
		
			// get response ...
			returned = GetPop3String();

			// if email does not exist at this position
			// then return false ...

			if( returned.Substring(0,4).Equals("-ERR") )
			{
				return false;
			}

			m_inboxPosition = pos;

			// strip out CRLF ...
			string[] noCr = returned.Split(new char[]{ '\r' });

			// get size ...
			string[] elements = noCr[0].Split(new char[]{ ' ' });

			long size = long.Parse(elements[2]);

			// ... else read email data
			m_pop3Message = new Pop3Message(m_inboxPosition,size,m_socket);

			return true;
		}

		public void openInbox()
		{
			// get a socket ...
			m_socket = GetClientSocket();

			// get initial header from POP3 server ...
			string header = GetPop3String();
            if (string.IsNullOrEmpty(header))
            {
                throw new Exception("Invalid initial POP3 response, header empty");
            }
            else if (header.Length<3) 
            {
                throw new Exception("Invalid initial POP3 response, header short");
            }
            else if (!header.Substring(0, 3).Equals("+OK"))
            {
                throw new Exception("Invalid initial POP3 response, header status not OK");
            }
			// send login details ...
			LoginToInbox();
		}
	}
}
