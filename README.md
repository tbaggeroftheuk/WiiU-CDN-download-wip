# 3ds-NUS-download-wip


# DISCLAMER

First things first aka you nintendo if you come snooping
This tool does not provide tickets
Users will have to provide tickets once I have that working
Does not bypass athentication on the NUS 
AND to be safe I will not be giving title ids you will have to find your own title ids for the game and or software you want

# Now thats out the way

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


# What it does

Connects to nintendos NUS server

tries to fetch specified titles .tmd file

Parses the .tmd to get the content file list 

Downloads all .app files

Verifies file intergrity with SHA256 (I hope this works)
Output stats and optionally json report 
