# WiiU-CDN-download-wip
![](https://komarev.com/ghpvc/?username=tbaggeroftheuk

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
havent tested much else if anyone wants to contribute feel free.
A quick note nus.py is legacy  

# Installation

First make sure you have python 3 installed
then run `pip install requests tqdm`
Grab the release from the release page 
and extract 

# How to use

Basic usage is this
in CMD cd to where you put the extracted the release
and then run 

`python CDN.py "insert title id"`

to download the id of the game you want.

If you want to decrypt make sure you are on windows you gave a valid ticket
and you wil need to put a wiiu common key into the keys.txt a google search for WiiU common key will get you one 

Flags

`--download-dir` where to save downloads default is that it makes a folder called downloads and downloads to there

`--force-redownload` files even if valid

`--verbose-show` detailed logs

`--quiet` supress normal output it only shows errors 

`--json` saves a .json report of downloaded files idk if this works

`--nohash` disables hash check 

`--h3 downloads` the h3 hash checking files idk why I added this or if it works 

`--no-organize` Dont move/copy files into subfolder or copy ticket if the ticket exits 

# about title keys (.tik)

IF you have any tickets (.tik) put them in /ticket also please make sure there named

with a title ID all that does if you download eg mario kart 8 and you have a mario kart 8 ticket

it will copy the content into a a folder /downloads/"title id" this wont work if --no-organize is on

this will work with any .tik you can used backed up tik files or ones off the internet I can not direct them to you though

# Credits

Someone on gbatemp https://gbatemp.net/threads/release-cdecrypt-v3-0.554220/
for the decryption exe I cant give the full name as gbatemp is down for me at the mo

# How to dump tickets from you own WiiU

YOU WILL NEED A HOMEBREWED WIIU

Btw yes I know tiramisu is legacy but the ticket dumper is only on legacy enviroment  

First grab tiramisu from https://tiramisu.foryour.cafe/

Take the Wiiu folder and dump it into the root of your SDcard
After that turn your wiiu and hold select this will open the envrioment loader I think 
Then open mii maker this will open the homebrew channel  if you have the homebrew app store open it  and search for ticket2sd install it 
go back to to the homebrew channel aka miimaker and launch ticket2sd follow its instructions
now thats done stick your sdcard into your computer go to to the sdcard and find a folder called ticket2sd
that will have subfolders and will have your tickets your welcome :)


