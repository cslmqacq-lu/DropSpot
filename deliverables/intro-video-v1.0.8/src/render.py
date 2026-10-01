import asyncio, subprocess, os, sys
from playwright.async_api import async_playwright
FPS=30; DUR=60.0
async def main():
    ff=subprocess.Popen(['ffmpeg','-y','-loglevel','error','-f','image2pipe','-framerate',str(FPS),'-c:v','mjpeg','-i','-',
        '-c:v','libx264','-preset','medium','-crf','18','-pix_fmt','yuv420p','video_noaudio.mp4'],stdin=subprocess.PIPE)
    async with async_playwright() as p:
        b=await p.chromium.launch()
        pg=await b.new_page(viewport={'width':1920,'height':1080})
        await pg.goto('file://'+os.path.abspath('intro.html'))
        await pg.wait_for_function('window.READY===true')
        await pg.evaluate('document.fonts.ready')
        n=int(DUR*FPS)
        for i in range(n):
            await pg.evaluate(f'seek({i/FPS})')
            img=await pg.screenshot(type='jpeg',quality=93)
            ff.stdin.write(img)
            if i%150==0: print(i, flush=True)
        await b.close()
    ff.stdin.close(); ff.wait()
asyncio.run(main())
