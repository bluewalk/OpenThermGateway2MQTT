using System;
using System.IO;
using System.Net.Sockets;

namespace Net.Bluewalk.OpenThermGateway2Mqtt.Tcp
{
    public class TcpClientAsync
    {
        #region Private variables

        private TcpClient client;
        private StreamWriter writer;
        private bool error = false;
        private bool mustClose = false;

        #endregion

        #region Public variables

        /// <summary>
        /// Occurs when data is read by the client.
        /// </summary>
        public event EventHandler<DataReceiveEventArgs> DataRead;

        /// <summary>
        /// Occurs when the client connects to the server.
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// Occurs when the client disconnects from the server.
        /// </summary>
        public event EventHandler Disconnected;

        /// <summary>
        /// Occurs when an exception is thrown during connection.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ClientConnectException;

        /// <summary>
        /// Occurs when an exception is thrown while reading data.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ClientReadException;

        /// <summary>
        /// Occurs when an exception is thrown while writing data.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ClientWriteException;

        #endregion

        public TcpClientAsync()
        {
        }

        /// <summary>
        /// Connects to the host and starts the reading thread
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        public async void Connect(string host, int port)
        {
            client = new TcpClient();

            try
            {
                await client.ConnectAsync(host, port);

                if (Connected != null)
                    Connected(this, new EventArgs());

                error = mustClose = false;
            }
            catch (SocketException ex)
            {
                error = true;

                if (ClientConnectException != null)
                    ClientConnectException(this, new ExceptionEventArgs(ex));
            }

            if (!error)
            {
                try
                {
                    using (var networkStream = client.GetStream())
                    using (writer = new StreamWriter(networkStream))
                    using (var reader = new StreamReader(networkStream))
                    {
                        writer.AutoFlush = true;

                        while (!mustClose && !error)
                        {
                            string data = await reader.ReadLineAsync();
                            if (!string.IsNullOrEmpty(data))
                            {
                                if (DataRead != null)
                                    DataRead(this, new DataReceiveEventArgs(data));
                            }
                            else
                                error = true;
                        }
                    }

                }
                catch (Exception ex)
                {
                    error = true;

                    if (ClientReadException != null)
                        ClientReadException(this, new ExceptionEventArgs(ex));
                }
            }

            if (client != null)
            {
                client.Close();

                if (Disconnected != null)
                    Disconnected(this, new EventArgs());
            }

            //if (error)
             //   if (ClientReadException != null)
              //      ClientReadException(this, new ExceptionEventArgs(new Exception("An error occurred while reading")));
        }

        /// <summary>
        /// Send data over the connecting
        /// </summary>
        /// <param name="data"></param>
        public async void Send(string data)
        {
            if (writer != null)
            {
                try
                {
                    await writer.WriteLineAsync(data);
                }
                catch (Exception ex)
                {
                    if (ClientWriteException != null)
                        ClientWriteException(this, new ExceptionEventArgs(ex));
                }
            }
        }

        /// <summary>
        /// Send data over the connecting
        /// </summary>
        /// <param name="data"></param>
        public async void Send(byte[] data)
        {
            try
            {
                await client.GetStream().WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                if (ClientWriteException != null)
                    ClientWriteException(this, new ExceptionEventArgs(ex));
            }
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            mustClose = true;
        }
    }


    /// <summary>
    /// Provides data for a data read event.
    /// </summary>
    public class DataReceiveEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new Data Read Event Args object.
        /// </summary>
        /// <param name=&amp;quot;data&amp;quot;>The data that was read from the remote host.</param>
        public DataReceiveEventArgs(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// Gets the data that has been read.
        /// </summary>
        public string Data { get; private set; }
    }

    ///// <summary>
    ///// Provides data for an exception occuring event.
    ///// </summary>
    public class ExceptionEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for a new Exception Event Args object.
        /// </summary>
        /// <param name=&amp;quot;ex&amp;quot;>The exception that was thrown.</param>
        public ExceptionEventArgs(Exception ex)
        {
            this.Exception = ex;
        }

        public Exception Exception { get; private set; }
    }
}
