Genera PNG alle dimensioni richieste:
	inkscape RRDA.RepImp/Assets/RepImp.svg --export-type=png --export-width=16 --export-filename=RepImp-16.png
	inkscape RRDA.RepImp/Assets/RepImp.svg --export-type=png --export-width=32 --export-filename=RepImp-32.png
	inkscape RRDA.RepImp/Assets/RepImp.svg --export-type=png --export-width=48 --export-filename=RepImp-48.png
	inkscape RRDA.RepImp/Assets/RepImp.svg --export-type=png --export-width=256 --export-filename=RepImp-256.png

Crea l'ICO:
	magick RepImp-16.png RepImp-32.png RepImp-48.png RepImp-256.png RepImp.ico