# HumanGateway User Guide

This guide is for people who read messages, answer human tasks, and attach evidence using the HumanGateway web app. It applies to local Edge use and authenticated remote access through a Relay.

## What HumanGateway Does

HumanGateway is durable, asynchronous communication. Messages and tasks are saved before delivery is attempted. You can continue working on the local network without Internet access. Delivery resumes when connectivity returns.

It is not live chat: a message may remain queued while a site or remote recipient is offline.

## Before You Begin

- Use a supported current Chrome, Edge, Firefox, or Safari browser.
- Connect to the site LAN for local use, or use the remote URL supplied by your administrator.
- Have the username and password supplied by your administrator.
- For attachments, allow the browser to access the camera or device files when prompted.

For a local development installation, the administrator must start the backend stack and the PWA before users can
sign in. The backend is normally available at `http://127.0.0.1:8080`, and the browser app is normally available at
`http://localhost:5173`. In a deployed site, use the address supplied by the administrator instead.

Administrators create user accounts and provide the login credentials. Users cannot create accounts themselves.

Administrators may see a **Users** option after signing in. That area is for account administration and is not needed
for normal messaging or task work.

## Sign In and Install

1. Open the HumanGateway address.
2. Enter your username and password.
3. Select **Sign in**.
4. On a supported browser, use the browser’s install option to add the PWA to your device.

The installed app can open its cached shell without Internet access. You still need to have signed in and loaded the relevant data previously for that data to be available offline.

## Conversations and Messages

Open a conversation to read its messages in chronological order. To send a message:

1. Open the conversation.
2. Enter the message text.
3. Optionally add an attachment.
4. Select **Send**.

The app saves the message locally first. Do not repeatedly press Send when the network is unavailable; the outbox will retry the queued operation.

## Tasks

Tasks are human interaction requests created by a workflow or another participant. An input task asks for text or information. An approval task asks you to approve or reject, usually with an optional reason.

1. Open the task from the task list.
2. Read the prompt and subject carefully.
3. Enter the requested response or choose the approval decision.
4. Add an attachment if evidence is requested.
5. Submit the response once.

The response is saved locally before it is sent. An expired task cannot be completed from the client; contact the workflow owner if the task needs to be reopened.

## Offline and Reconnect Behavior

The sync banner identifies the client state:

- **Online:** the browser can reach its configured service.
- **Offline:** the browser has no network connection; local data and supported compose/task actions remain available.
- **Reconnecting:** connectivity has returned and queued work is being flushed.

When the site Internet is down, local LAN work can continue. Remote messages and acknowledgements wait until the Edge can synchronize with the Relay. Keep the app open long enough for a queued response to be saved; once saved, it is not dependent on the current browser tab.

## Statuses and Notifications

- **Queued:** saved locally and waiting for a delivery attempt.
- **Waiting for sync:** the service is waiting for connectivity or a retry window.
- **Syncing:** a delivery attempt is in progress.
- **Delivered:** the recipient service has accepted the message.
- **Acknowledged:** the sender has received the delivery acknowledgement.
- **Failed:** delivery stopped after the configured retry policy; contact an administrator.

These statuses describe delivery, not whether a recipient has read a message.

## Attachments

HumanGateway treats photos, PDFs, documents, and audio as artifacts referenced by a message or task response. Select an allowed file from the attachment control and wait for the upload state to complete before leaving the page.

The default maximum artifact size is 50 MiB and the default per-gateway quota is 1 GiB. An administrator may configure different limits. If an upload is interrupted, the system can resume it; if the quota or size limit is exceeded, remove an unnecessary attachment or contact the administrator.

## Accessibility and Mobile Use

Use keyboard navigation and visible focus indicators on desktop. On mobile, use a current browser and allow enough free storage for the PWA cache and offline data. Statuses are represented by text as well as visual styling, so do not rely on color alone.

## Privacy and Data Handling

The service stores message content, task responses, conversation metadata, and attachments so it can deliver them. Do not include information that your site policy does not allow. Retention and deletion are controlled by the deploying site and, for workflow records, by the consuming workflow system.

## Troubleshooting

**The app says offline.** Confirm Wi-Fi or mobile data, then wait for the reconnecting state. For local use, verify that you are on the site LAN.

**A message stays queued.** This is expected during a disconnected period. If it remains queued after connectivity returns, note the message status and contact the administrator.

**An attachment is rejected.** Check the file size and type, then check whether the site quota is full.

**A task is missing.** Confirm that you are signed in as the assigned user and refresh after reconnecting. Ask the workflow owner to verify task assignment.

**A response is marked failed.** Do not submit duplicates immediately. Contact the administrator with the task or message identifier and visible error text.

## Getting Help

Provide your username, gateway/site name, approximate time, message or task ID, status shown by the app, and whether the device was online. Never send your password or a session token.
