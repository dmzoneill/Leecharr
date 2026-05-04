# BitTorrent Protocol Implementations in Leecharr

## Protocol Stack

```mermaid
graph TB
    subgraph "Application Layer"
        ENGINE["Torrent Engine & Piece Picker"]
        DISK["Async Disk I/O & Hasher"]
        MEDIA["Media Stream Inspector"]
    end

    subgraph "BitTorrent Protocol Layer"
        TRACK["Trackers (HTTP / UDP / Multi-Tracker)<br/>BEP 3, 12, 15"]
        PEER["Peer Wire Protocol<br/>BEP 3"]
        EXT["Extensions (PEX, ut_metadata, Fast, LPD)<br/>BEP 6, 9, 10, 11, 14"]
        DHT["Kademlia DHT<br/>BEP 5"]
    end

    subgraph "Security Layer"
        MSE["MSE/PE Stream Encryption<br/>(DH 768-bit + RC4)"]
    end

    subgraph "Transport Layer"
        TCP["TCP Socket Stream"]
        UTP["uTP (Micro Transport Protocol)<br/>BEP 29 (LEDBAT)"]
    end

    ENGINE --> DISK
    ENGINE --> TRACK
    ENGINE --> PEER
    PEER --> EXT
    PEER --> MSE
    MSE --> TCP
    MSE --> UTP
    TRACK --> TCP
    TRACK --> UTP
    DHT --> UTP
```

## BEP Standards Implemented

- **BEP 3 (BitTorrent Protocol Specification):** Peer handshake, KeepAlive, Choke/Unchoke, Interested/NotInterested, Have, Bitfield, Request (16 KB blocks), Piece, Cancel.
- **BEP 5 (Distributed Hash Table - DHT):** Kademlia routing table, compact node info, KRPC `ping`, `find_node`, `get_peers`, `announce_peer`.
- **BEP 6 (Fast Extension):** `AllowedFast` set calculation, `SuggestPiece`, `RejectRequest`, `HaveAll`, `HaveNone`.
- **BEP 9 (Metadata Exchange - `ut_metadata`):** Magnet link resolution and piece-by-piece metadata downloading over peer connections.
- **BEP 10 (Extension Protocol):** Standardized dictionary handshake (`m` map) and extended message framing.
- **BEP 11 (Peer Exchange - PEX):** `ut_pex` for fast swarm discovery.
- **BEP 12 (Multi-Tracker Extension):** Tiered announce lists and failover logic.
- **BEP 14 (Local Peer Discovery - LPD):** Multicast announces over `239.192.152.143:6771`.
- **BEP 15 (UDP Tracker Protocol):** Binary 98-byte connect and announce protocol with transaction validation.
- **BEP 29 (uTP Transport):** User Datagram Protocol transport with LEDBAT congestion control and TCP fallback.
- **MSE / PE Encryption:** Diffie-Hellman 768-bit key negotiation and RC4 stream encryption with initial 1024-byte discard.
