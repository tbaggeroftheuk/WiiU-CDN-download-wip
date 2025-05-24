# WiiU-CDN-download-wip


# DISCLAMER

This tool does not provide decryption keys, tickets, or title IDs.

Users must supply their own legally obtained tickets if required.

This project does not bypass authentication on Nintendo's NUS servers, nor does it promote or enable piracy.

This tool is intended for educational and archival purposes only.

If you are a rights holder and have concerns, please contact me or file a takedown through the appropriate GitHub process.

# Now thats out the way
Also you will see in previous versions of nus.py that this was orginally 3ds
And then I found out the 3ds cdn was bassicly unplugged so this is now Wii U 
This is my work in progress nus downloader
some stuff doesnt work downloading eg the system settings app from my inital test did not work
game names also doesnt work
havent tested much else if anyone wants to contribute feel free 
my main goal is to make tickets work so you can actually get a piece of software 

# Installation

First make sure you have python 3 installed
then run `pip install requests tqdm`
Grab nus.py from the source code or release where ever I have put it

# How to use

Basic usage is this
in CMD cd to where you put the file
and then run 

`python nus.py "insert title id"`

to download the id of the game you want

Flags

`--download-dir` where to save downloads default is that it makes a folder called downloads and downloads to there

`--force-redownload` files even if valid

`--verbose-show` detailed logs

`--quiet` supress normal output it only shows errors 

`--json` saves a .json report of downloaded files idk if this works

`--nohash` disables hash check 

# What it does

Connects to nintendos NUS server

tries to fetch specified titles .tmd file

Parses the .tmd to get the content file list 

Downloads all .app files

Verifies file intergrity with SHA256 (I hope this works)
Output stats and optionally json report 
