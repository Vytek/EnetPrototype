using System.Collections.Generic;
using System.Text;
using ENet;
using NetStack.Serialization;
using Threaded;
using TMPro;
using UnityEngine;

namespace NextSimple
{
    public partial class BaseEntity : NetworkedEntity
    {
        private bool m_isServer = false;
        private bool m_isLocal = false;

        private ClientNetworkSystem m_client = null;
        private ServerNetworkSystem m_server = null;

        [SerializeField] private TextMeshPro m_text = null;
        [SerializeField] private Material m_clientRemoteMat = null;
        [SerializeField] private Material m_clientLocalMat = null;
        [SerializeField] private Material m_serverMat = null;
        [SerializeField] private Renderer m_renderer = null;

        private float m_updateRate = 0.1f;
        private readonly BitBuffer m_buffer = new BitBuffer(128);
        private float m_nextUpdate = 0f;

        private readonly SynchronizedFloat m_randomValue = new SynchronizedFloat();
        private readonly SynchronizedString m_stringValue1 = new SynchronizedString();
        private readonly SynchronizedASCII m_stringValue2 = new SynchronizedASCII();

        //private readonly List<ISynchronizedVariable> m_syncs = new List<ISynchronizedVariable>();

        public Renderer Renderer => m_renderer;

        public Vector4? m_newPos = null;

        public uint Id { get; private set; }
        public Peer Peer { get; private set; }

        #region MONO

        protected override void Awake()
        {
            base.Awake();
            Subscribe();
        }

        void Update()
        {
            LerpPositionRotation();
            UpdateSyncVars();
            UpdateLocal();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void OnMouseDown()
        {
            if (m_isServer)
            {
                GenRandomString1();
            }
        }

        #endregion

        #region INIT

        private void Subscribe()
        {
            m_randomValue.Changed += RandomValChanged;
            m_stringValue1.Changed += StringValChanged1;
            m_stringValue2.Changed += StringValChanged2;
        }

        private void Unsubscribe()
        {
            m_randomValue.Changed -= RandomValChanged;
            m_stringValue1.Changed -= StringValChanged1;
            m_stringValue2.Changed -= StringValChanged2;            
        }

        public void Initialize(Peer peer, uint id)
        {
            Peer = peer;
            Id = id;
            gameObject.transform.position = GetRandomPos();
            gameObject.name = $"{Id} (SERVER)";
            m_renderer.material = m_serverMat;
            m_isServer = true;
            m_text.SetText(Id.ToString());
        }

        public void Initialize(uint id, Peer peer, BitBuffer buffer)
        {
            Peer = peer;
            Id = id;
            
            gameObject.name = $"{Id} (CLIENT)";
            m_renderer.material = m_clientRemoteMat;

            var pos = buffer.ReadVector3(SharedStuff.Instance.Range);
            var rot = Quaternion.Euler(new Vector3(0f, buffer.ReadFloat(), 0f));
            gameObject.transform.SetPositionAndRotation(pos, rot);

            for (int i = 0; i < m_syncs.Count; i++)
            {
                m_syncs[i].ReadVariable(buffer);
            }
        }

        #endregion

        #region UPDATES

        private void LerpPositionRotation()
        {
            if (m_newPos.HasValue)
            {
                var targetRot = Quaternion.Euler(new Vector3(0f, m_newPos.Value.w, 0f));
                m_renderer.gameObject.transform.rotation = Quaternion.Lerp(m_renderer.gameObject.transform.rotation,
                    targetRot, Time.deltaTime * 2f);
                gameObject.transform.position =
                    Vector3.Lerp(gameObject.transform.position, m_newPos.Value, Time.deltaTime * 2f);
                if (Vector3.Distance(gameObject.transform.position, m_newPos.Value) < 0.1f)
                {
                    m_newPos = null;
                }
            }
        }

        //TODO: change this to an AddDirtySyncVars and have a manager handle the updates
        private void UpdateSyncVars()
        {
            if (m_isServer == false || CanUpdate() == false)
                return;

            m_nextUpdate += m_updateRate;

            int dirtyBits = 0;

            for (int i = 0; i < m_syncs.Count; i++)
            {
                if (m_syncs[i].Dirty)
                {
                    dirtyBits = dirtyBits | m_syncs[i].BitFlag;
                }
            }

            if (dirtyBits == 0)
                return;

            var buffer = m_buffer;
            buffer.AddEntityHeader(Peer, OpCodes.SyncUpdate);
            buffer.AddInt(dirtyBits);

            for (int i = 0; i < m_syncs.Count; i++)
            {
                if (m_syncs[i].Dirty)
                {
                    buffer.AddSyncVar(m_syncs[i]);
                }
            }

            var packet = buffer.GetPacketFromBuffer(PacketFlags.Reliable);
            var command = GameCommandPool.GetGameCommand();
            command.Type = CommandType.BroadcastAll;
            command.Packet = packet;
            command.Channel = 0;

            Debug.Log($"Sending dirtyBits: {dirtyBits}  Length: {packet.Length}");

            m_server.AddCommandToQueue(command);
        }
        
        public BitBuffer AddAllSyncData(BitBuffer buffer)
        {
            for (int i = 0; i < m_syncs.Count; i++)
            {
                buffer.AddSyncVar(m_syncs[i], false);
            }
            return buffer;
        }

        private void UpdateLocal()
        {
            if (m_isLocal == false)
                return;

            if (m_newPos.HasValue == false)
            {
                m_newPos = GetRandomPos();
            }

            if (CanUpdate())
            {
                m_buffer.AddEntityHeader(Peer, OpCodes.PositionUpdate);
                m_buffer.AddVector3(gameObject.transform.position, SharedStuff.Instance.Range);
                m_buffer.AddFloat(m_renderer.gameObject.transform.eulerAngles.y);

                var command = GameCommandPool.GetGameCommand();
                command.Type = CommandType.Send;
                command.Packet = m_buffer.GetPacketFromBuffer();
                command.Channel = 0;

                m_client.AddCommandToQueue(command);

                m_nextUpdate = Time.time + 0.1f;
            }
        }

        #endregion


        private Vector4 GetRandomPos()
        {
            return new Vector4(
                Random.Range(-1f, 1f) * SharedStuff.Instance.RandomRange,
                Random.Range(-1f, 1f) * SharedStuff.Instance.RandomRange,
                Random.Range(-1f, 1f) * SharedStuff.Instance.RandomRange,
                Random.Range(0, 1f) * SharedStuff.Instance.RandomRange * 360f);
        }

        public void AssumeOwnership()
        {
            m_isLocal = true;
            gameObject.name = $"{gameObject.name} OWNER";
            m_renderer.material = m_clientLocalMat;
            m_text.SetText("Local");
        }

        private void RandomValChanged(float value)
        {
            if (m_isServer == false)
            {
                Debug.Log($"RandomVal Changed to {value}!");
                m_randomValue.Value = value;
            }
        }

        private void StringValChanged1(string value)
        {
            if (m_isServer == false)
            {
                Debug.Log($"StringVal1 Changed to {value}!");
                m_stringValue1.Value = value;
                if (value.Length >= 8)
                {
                    m_text.SetText(m_stringValue1.Value.Substring(0, 8));   
                }
            }
        }

        private void StringValChanged2(string value)
        {
            if (m_isServer == false)
            {
                Debug.Log($"StringVal2 Changed to {value}!");
                m_stringValue2.Value = value;
            }
        }

        public void ProcessSyncUpdate(BitBuffer buffer)
        {
            int dirtyBits = buffer.ReadInt();

            Debug.Log($"Received dirtyBits: {dirtyBits}");

            if (dirtyBits == 0)
                return;

            for (int i = 0; i < m_syncs.Count; i++)
            {
                if ((dirtyBits & m_syncs[i].BitFlag) == m_syncs[i].BitFlag)
                {
                    m_syncs[i].ReadVariable(buffer);
                }
            }
        }

        private bool CanUpdate()
        {
            return Peer.IsSet && Time.time > m_nextUpdate;
        }

        #region TEMPORARY_FOR_TESTING

        [ContextMenu("Generate Random Value")]
        private void GenRandomVal()
        {
            if (m_isServer == false)
                return;
            float prev = m_randomValue.Value;
            m_randomValue.Value = Random.Range(0f, 1f);
            Debug.Log($"Generated from {prev} to {m_randomValue.Value}");
        }

        public int stringIterations = 10;

        [ContextMenu("Generate Random String1")]
        private void GenRandomString1()
        {
            if (m_isServer == false)
                return;
            string prev = m_stringValue1.Value;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < stringIterations; i++)
            {
                sb.Append(System.Guid.NewGuid().ToString());
            }

            m_stringValue1.Value = sb.ToString();
            Debug.Log($"Generated from {prev} to {m_stringValue1.Value}");
        }

        [ContextMenu("Generate Random String2")]
        private void GenRandomString2()
        {
            if (m_isServer == false)
                return;
            string prev = m_stringValue2.Value;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < stringIterations; i++)
            {
                sb.Append(System.Guid.NewGuid().ToString());
            }

            m_stringValue2.Value = sb.ToString();
            Debug.Log($"Generated from {prev} to {m_stringValue2.Value}");
        }

        [ContextMenu("Generate Random Value & String")]
        private void GenRandomValStrings()
        {
            GenRandomVal();
            GenRandomString1();
            GenRandomString2();
        }


        #endregion
    }
}