using PCSC;
using PCSC.Monitoring;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sydesoft.NfcDevice
{
    /// <summary>
    /// Require https://github.com/danm-de/pcsc-sharp
    /// </summary>
    public class ACR122U
    {
        private int maxReadWriteLength = 50;
        private int blockSize = 4;
        private int startBlock = 4;
        private int readbackDelayMilliseconds = 100;
        private string[] cardReaderNames = null;
        private ISCardContext cardContext = null;
        private bool buzzerSet = false;
        private bool buzzerOnOff = false;

        public event CardInsertedHandler CardInserted;
        public delegate void CardInsertedHandler(ICardReader reader);

        public event CardRemovedHandler CardRemoved;
        public delegate void CardRemovedHandler();

        public void Init(bool buzzerOnOff, int maxReadWriteLength, int blockSize, int startBlock, int readbackDelayMilliseconds)
        {
            this.buzzerOnOff = buzzerOnOff;
            this.maxReadWriteLength = maxReadWriteLength;
            this.blockSize = blockSize;
            this.startBlock = startBlock;
            this.readbackDelayMilliseconds = readbackDelayMilliseconds;

            cardContext = ContextFactory.Instance.Establish(SCardScope.System);

            cardReaderNames = cardContext.GetReaders();

            var monitor = MonitorFactory.Instance.Create(SCardScope.System);
            monitor.CardInserted += Monitor_CardInserted;
            monitor.CardRemoved += Monitor_CardRemoved;
            monitor.Start(cardReaderNames);
        }

        private void Monitor_CardInserted(object sender, CardStatusEventArgs e)
        {
            ICardReader reader = null;

            try
            {
                reader = cardContext.ConnectReader(cardReaderNames[0], SCardShareMode.Shared, SCardProtocol.Any);
            }
            catch { }

            if (reader != null)
            {
                if (!buzzerSet)
                {
                    buzzerSet = true;
                    SetBuzzer(reader, buzzerOnOff);
                }

                CardInserted?.Invoke(reader);

                try
                {
                    reader.Disconnect(SCardReaderDisposition.Leave);
                }
                catch { }
            }
        }

        private void Monitor_CardRemoved(object sender, CardStatusEventArgs e)
        {
            CardRemoved?.Invoke();
        }

        public byte[] GetUID(ICardReader reader)
        {
            byte[] uid = new byte[10];

            reader.Transmit(new byte[] { 0xFF, 0xCA, 0x00, 0x00, 0x00 }, uid);

            Array.Resize(ref uid, 7);

            return uid;
        }

        public byte[] Read(ICardReader reader, int block, int len)
        {
            byte[] data = new byte[len + 2];

            reader.Transmit(new byte[] { 0xFF, 0xB0, 0x00, (byte)block, (byte)len }, data);

            Array.Resize(ref data, len);

            return data;
        }

        public void Write(ICardReader reader, int block, int len, byte[] data)
        {
            byte[] ret = new byte[2];
            List<byte> cmd = new byte[] { 0xFF, 0xD6, 0x00, (byte)block, (byte)len }.ToList();
            cmd.AddRange(data);

            reader.Transmit(cmd.ToArray(), ret);
        }

        public bool WriteData(ICardReader reader, byte[] data)
        {
            Array.Resize(ref data, maxReadWriteLength);

            int pos = 0;
            while (pos < data.Length)
            {
                byte[] buf = new byte[blockSize];
                int len = data.Length - pos > blockSize ? blockSize : data.Length - pos;
                Array.Copy(data, pos, buf, 0, len);

                Write(reader, (pos / blockSize) + startBlock, blockSize, buf);

                pos += blockSize;
            }

            Thread.Sleep(readbackDelayMilliseconds);

            byte[] readback = ReadData(reader);

            return data.SequenceEqual(readback);
        }

        public byte[] ReadData(ICardReader reader)
        {
            List<byte> data = new List<byte>();

            int pos = 0;
            while (pos < maxReadWriteLength)
            {
                int len = maxReadWriteLength - pos > blockSize ? blockSize : maxReadWriteLength - pos;

                byte[] buf = Read(reader, (pos / blockSize) + startBlock, len);

                data.AddRange(buf);

                pos += blockSize;
            }

            return data.ToArray();
        }

        public void SetBuzzer(ICardReader reader, bool on)
        {
            byte[] ret = new byte[2];

            reader.Transmit(new byte[] { 0xFF, 0x00, 0x52, (byte)(on ? 0xff : 0x00), 0x00 }, ret);
        }
    }
}