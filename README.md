# SteamStreaming

## What is it?

This project was my attempt at reimplementing the Steam In-Home Streaming protocol in a [clean-room fashion](https://en.wikipedia.org/wiki/Clean_room_design) (i.e. without ever opening the original code in a decompiler) by just looking at the raw bytes in a packet dump. Since the protocol is quite similar to TCP with an additional unreliable channel and based on protobufs [which are known](https://github.com/SteamDatabase/Protobufs/tree/master/steam), doing so was not only possible, but I believe I managed to reverse-engineer 95% of it. The only part I needed some help with was the encryption protocol of the control packets and the hash in the auth message (both of which turned out to be pretty simple).

## What is the current state of the project?

The code in this repository contains manages to start the streaming server and receive audio and video streams, albeit the video usually drops after a while - I have no idea why, I presume it has to be something related to ffmpeg parameters. Inputs are not implemented yet. I didn't have enough time and motivation to continue the project for a while, so I decided to release it as-is hoping that it may help someone who is trying to achieve a similar thing. Mostly working dissector for Wireshark is also included. The most complete interface is in the XSteamBox project.

## What next?

The current code is of a pretty proof-of-concept quality and definitely needs some cleaning up. One of my goals was to write a client for Xbox One, that is not really implemented either. I never tested it on an actual network, only on localhost. If you are willing to help, I'll gladly accept any pull requests.