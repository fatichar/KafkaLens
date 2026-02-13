---
layout: default
title: Usage Guide
nav_order: 3
description: "Master KafkaLens with our comprehensive usage guide."
---

# Usage Documentation
{: .fs-9 }

Get the most out of KafkaLens with this step-by-step guide.

---

## 🏗️ Getting Started

### 1. Adding your first Cluster
To begin, you need to tell KafkaLens where your Kafka brokers are located.
1. Click the **Edit Clusters** button in the sidebar.
2. Click **Add Cluster**.
3. Enter a friendly name and your bootstrap servers (e.g., `localhost:9092`).
4. Click **Save**.

### 2. Navigation
The sidebar contains all your configured clusters. Click a cluster to connect. Once connected, you can browse:
- **Topics**: All available topics in the cluster.
- **Partitions**: Drill down into specific partitions to see message distribution.

---

## 🔍 Exploring Data

### Browsing Messages
Double-click a topic or partition to open a new data tab.
- **Fetch**: Click "Load" to fetch the most recent messages.
- **Seek**: Use the offset controls to jump to a specific point in time or a specific offset.

### Message Inspection
Select any message to view its details in the side panel. KafkaLens provides specialized formatters:
- **JSON**: Pretty-prints JSON payloads for easy reading.
- **String**: Displays the raw UTF-8 string.
- **Hex**: Perfect for debugging binary or proprietary formats.

---

## 💾 Advanced Features

### Saving Messages
Found a message you need to keep? Right-click any message in the list and select **Save Message**. This will export the full message (including headers and metadata) into a `.klm` file.

### Filtering
Use the search bar in the message view to filter through loaded messages. You can use simple text matching or advanced boolean logic (e.g., `error && !timeout`).

---

## ❓ Need Help?

If you encounter issues or have feature requests, please open an issue on our [GitHub repository](https://github.com/fatichar/KafkaLens/issues).
