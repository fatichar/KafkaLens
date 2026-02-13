---
layout: default
title: Usage
nav_order: 3
description: "Learn how to use KafkaLens to manage your Kafka clusters."
---

# Usage Documentation

Welcome to the KafkaLens guide. This page will help you get started with the main features of the application.

## 1. Adding a Cluster

To start browsing messages, you first need to add a Kafka cluster:

1. Click on the **Edit Clusters** button (or the plus icon in the sidebar).
2. Enter a **Name** for your cluster (e.g., "Production").
3. Provide the **Bootstrap Servers** (e.g., `localhost:9092`).
4. Click **Save**.

Your cluster should now appear in the sidebar.

## 2. Connecting to a Cluster

Click on a cluster in the sidebar to connect to it. KafkaLens will fetch the list of available topics and partitions.

## 3. Browsing Topics

Once connected:
- Expand the cluster to see the list of **Topics**.
- Select a topic to view its partitions.
- Double-click a topic or partition to open a new tab for browsing messages.

## 4. Viewing Messages

In the message browser tab:
- Click **Load** to fetch the latest messages.
- You can filter messages by partition or search for specific content.
- Select a message to see its full **Key**, **Value**, and **Headers** in the details pane.

### Message Formatters
KafkaLens supports various formatters to help you read message content:
- **String**: Raw text representation.
- **JSON**: Pretty-printed JSON if the content is a valid JSON string.
- **Hex**: Hexadecimal view for binary data.

You can change the formatter for both Keys and Values in the message details view.

## 5. Saving Messages

You can save individual messages to your local machine for later analysis:
1. Right-click on a message in the list.
2. Select **Save Message**.
3. Choose a location to save the `.klm` file.

---

## Tips
- Use the **Refresh** button to update the list of topics if new ones were created.
- You can open multiple clusters simultaneously in different tabs.
