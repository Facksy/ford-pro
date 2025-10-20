using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;

namespace ConsoleApp1
{
    class Program
    {
        private static readonly ConcurrentQueue<byte[]> queue = new ConcurrentQueue<byte[]>();

        static void Main(string[] args)
        {
            xMain();
        }

        static async void xMain()
        {
            while (true)
            {
                try
                {
                    string choice = "";
                    while (choice != "h" && choice != "c")
                    {
                        Console.WriteLine("Do you want to host(h) or connect (c)?");
                        choice = Console.ReadLine();
                    }

                    if (choice == "h")
                    {
                        TcpListener listener = new TcpListener(IPAddress.Parse("0.0.0.0"), 27015);
                        listener.Start();
                        Console.WriteLine("Listening...");

                        TcpClient client = listener.AcceptTcpClient();
                        client.ReceiveBufferSize = 8192;
                        client.SendBufferSize = 8192;
                        NetworkStream ns = client.GetStream();
                        Console.WriteLine("Received client!");

                        while (choice != "r" && choice != "s")
                        {
                            Console.WriteLine("Do you receive (r) or send (s) files?");
                            choice = Console.ReadLine();
                        }

                        if (choice == "r")
                        {
                            while (true)
                            {
                                Console.WriteLine("You are the receiver, waiting for sender input...");
                                Write(ns, "SENDER");
                                ns.ReadByte();

                                await StartReceiving(ns);
                            }
                        }
                        else
                        {
                            while (true)
                            {
                                Console.WriteLine("You are the sender!");
                                Write(ns, "RECEIVER");
                                ns.ReadByte();
                                await StartSending(ns);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Enter ip adress (127.0.0.1):");
                        string ip = Console.ReadLine();
                        if (ip.Split('.').Length != 3)
                            ip = "127.0.0.1";
                        Console.WriteLine("Enter port (27015):");
                        if (!int.TryParse(Console.ReadLine(), out int port))
                            port = 27015;

                        Console.WriteLine($"Trying to connect to {ip}:{port} ...");
                        TcpClient client = new TcpClient();

                        try
                        {
                            client.Connect(ip, port);
                            Console.WriteLine($"Connected! waiting for host directive...");

                            NetworkStream ns = client.GetStream();

                            string res = Read(ns, 64);
                            if (res == "RECEIVER")
                            {
                                Console.WriteLine("You are the receiver!");
                                ns.WriteByte(1);

                                await StartReceiving(ns);
                            }
                            else if (res == "SENDER")
                            {
                                Console.WriteLine("You are the sender!");
                                ns.WriteByte(1);

                                await StartSending(ns);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                            Console.ReadKey();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    Console.ReadKey();
                }
            }
        }

        private static string GetHash(string filepath)
        {
            using (MD5 alg = MD5.Create())
            {
                using (FileStream stream = File.OpenRead(filepath))
                {
                    byte[] hash = alg.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        
        private static string Read(NetworkStream ns, int size)
        {
            byte[] buff = new byte[size];
            int len = ns.Read(buff, 0, size);
            if (len == 0)
                throw new Exception("CLOSED");
            return Encoding.ASCII.GetString(buff, 0, len);
        }

        private static void Write(NetworkStream ns, string str)
        {
            byte[] buff = Encoding.ASCII.GetBytes(str);
            ns.Write(buff, 0, buff.Length);
        }

        private static async Task StartReceiving(NetworkStream ns)
        {
            string choice = "";
            byte[] buffer;
            int len;

            string res = Read(ns, 256);
            string[] param = res.Split('|');

            Console.WriteLine($"You are about to receive folder {param[0]} with size {param[1]}");
            while (choice != "y" && choice != "n")
            {
                Console.WriteLine($"Do you accept y/n?");
                choice = Console.ReadLine();
            }
            if (choice == "y")
            {
                Write(ns, "ACCEPTED");

                string root = param[0];

                while (true)
                {
                    res = Read(ns, 256);
                    param = res.Split('|');
                    string fpath = Path.GetFileName(param[0].Replace(root, ""));
                    fpath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", fpath);

                    Console.WriteLine($"Received {res}");

                    if (res == "END")
                        break;

                    if (File.Exists(fpath))
                    {
                        Console.WriteLine($"File {fpath} exists...");
                        string hash = GetHash(fpath);
                        if (hash != param[1])
                        {
                            Console.WriteLine($"...but bad checksum -> delete");
                            File.Delete(fpath);
                        }
                        else
                        {
                            Console.WriteLine($"...and good checksum -> skip");
                            Write(ns, "SKIP");
                            continue;
                        }
                    }

                    Write(ns, "GO");

                    if (!Directory.Exists(Path.GetDirectoryName(fpath)))
                        Directory.CreateDirectory(Path.GetDirectoryName(fpath));

                    using (FileStream fs = File.Create(fpath))
                    {
                        buffer = new byte[8192];
                        bool run = true;

                        Task t = Task.Run(async () =>
                        {
                            while (run || queue.Count > 0)
                            {
                                if (queue.TryDequeue(out byte[] b))
                                    fs.Write(b, 0, b.Length);
                                else
                                    await Task.Delay(100);
                            }
                        });

                        while (true)
                        {
                            len = ns.Read(buffer, 0, buffer.Length);
                            if (len == 0)
                                throw new Exception("CLOSED");
                            if (len <= 8 && Encoding.ASCII.GetString(buffer, 0, len) == "ENDF")
                            {
                                run = false;
                                break;
                            }
                            byte[] buff2 = new byte[len];
                            Array.Copy(buffer, buff2, len);
                            queue.Enqueue(buff2);
                        }

                        run = false;

                        await t;

                        Console.WriteLine("Ended receiving! sending signal");
                        ns.WriteByte(1);
                    }
                    Write(ns, "FINISHED");
                }
            }
            else
                Console.WriteLine("not accepted...");
        }

        private static async Task StartSending(NetworkStream ns)
        {
            string choice = "", res;
            byte[] buffer;
            int len;

            while (!Directory.Exists(choice))
            {
                Console.WriteLine("Provide a directory path:");
                choice = Console.ReadLine();
            }

            Write(ns, $"{choice}|121212");

            res = Read(ns, 64);

            if (res == "ACCEPTED")
            {
                Console.WriteLine("He accepted");
                foreach (string file in Directory.GetFiles(choice))
                {
                    string hash = GetHash(file);

                    Console.WriteLine($"Going with {file}|{hash}...");
                    Write(ns, $"{file}|{hash}");

                    res = Read(ns, 64);
                    Console.WriteLine($"He said {res}");
                    Thread.Sleep(5000);
                    switch (res)
                    {
                        case "SKIP":
                            continue;

                        case "GO":
                            using (FileStream fs = File.OpenRead(file))
                            {
                                buffer = new byte[8192];
                                bool run = true;

                                Task t = Task.Run(async () =>
                                {
                                    while (run || queue.Count > 0)
                                    {
                                        if(queue.TryDequeue(out byte[] b))
                                            ns.Write(b, 0, b.Length);
                                        else
                                            await Task.Delay(100);
                                    }
                                });

                                while ((len = fs.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    byte[] buff2 = new byte[len];
                                    Array.Copy(buffer, buff2, len);
                                    queue.Enqueue(buff2);
                                }
                                run = false;

                                await t;
                                
                                buffer = Encoding.ASCII.GetBytes($"ENDF");
                                ns.Write(buffer, 0, buffer.Length);
                                Console.WriteLine("Ended sending... waiting signal...");
                                ns.ReadByte();
                            }
                            break;
                    }
                }
            }
            else
                Console.WriteLine("not accepted...");
        }
    }
}
