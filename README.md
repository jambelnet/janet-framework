# jaNET Framework

## Introduction

A free and open source IoT framework that provides a set of built-in [functions](https://github.com/jambelnet/janet-framework/wiki/Built-in-functions), a native API ([judo API](https://github.com/jambelnet/janet-framework/wiki/judo-API)), and multiple providers, such as scheduler, evaluator, notification manager and others, to allow a 3rd party software (e.g. [Jubito](http://www.jubito.org), see details below) to exploit, in order to interact with multiple services, software applications and vendor hardware (especially open hardware, such as Arduino, Raspberry Pi, Banana Pi, etc). It is designed for interoperability, therefore, to be absolutely vendor-neutral as well as hardware/protocol-agnostic. It can operate on any device that is capable of running .NET Framework or Mono (Linux, Windows, Mac, including single-board computers, such Raspberry Pi and Banana Pi).

## Usage

1. Clone the repository, open the solution with Visual Studio or MonoDevelop and build it.
2. Copy '*www*' root directory inside your build folder, e.g. *janet-framework\jaNETProgram\bin\Debug*.
3. Run the application (*jaNETProgram.exe*) and access Jubito UI (*http:/localhost:8080/www/*)[*1*] in your browser.

[*1*] Default built-in web server provided by the framework is listening to localhost on port 8080.

You can change defaults by corresponding UI menu (*Menu->Settings->Web Server*) or via judo API.

> judo server setup [host] [port] [authentication]\
i.e.
> judo server setup localhost 8080 none|basic

[judo API doc](https://github.com/jambelnet/janet-framework/wiki/judo-API)

## Structure

There's basically two components in the core system:

* Instruction Sets
* Events

## Help

A forum wil be started at some point.\
Submit bugs or feature requests [here](https://github.com/jambelnet/janet-framework/issues) and turn yourself into a valuable project participant.

## Requirements

### Windows
* .NET Framework > 4.0
* Visual Studio (any version)

### Linux
* mono-complete
* [mono-develop](http://www.monodevelop.com)

## Configuration

All system configuration are described in *System* tag within *AppConfig.xml*.\
They can manipulated by judo API, but I suggest you doing it, either by editing the XML or by the web UI (*Menu->Settings*).

## Hardware & Software Compatibility

It is fully tested and runnable on devices listed below:

* Any computer with Windows or Linux desktops
* Raspberry Pi 3 Model B
* Banana Pi
* Banana Pro
* BPi-R1

**Attached microcontrollers**:

* Arduino
* [RaZberry](http://razberry.z-wave.me/)

**Examples**:

* [Arduino](http://jubitoblog.blogspot.com/search/label/arduino)
* [RazBerry](http://jubitoblog.blogspot.com/search/label/razberry)
* [IP Camera](http://jubitoblog.blogspot.com/2013/02/dvr-system-using-ip-camera.html)

## Contributing

Any kind of contribution is always very welcome and appreciated.\
Once you're familiar with the way jaNET works then you might want to contribute to the core system.

## Contact

You may reach out to me via [email](mailto:jambel@jubito.org) or [contact form](http://www.jubito.org/contact.html).

## License

This project is licensed under GNU General Public License (http://www.gnu.org/licenses/).

## Wiki
https://github.com/jambelnet/janet-framework/wiki

# About Jubito
[Jubito](http://www.jubito.org) is a complete DIY automation solution. An awarded IoT hub ([Technical Enabler: Application Enablement](http://www.postscapes.com/internet-of-things-award/2014/iot-application-enabler/) - [Honors & Awards](http://jubitoblog.blogspot.com/search/label/awards)) based on jaNET Framework.
To get a deeper understanding on how the web application layer sits on top, and implements the framework, [download](http://www.jubito.org/download.html) Jubito, open index.html and js/jubito.core.js files and read through the code. They are located on the /www/ root directory. A copy of it, can be found on this git as well.
Afterwards you'll be able to create [custom widgets and more](http://jubitoblog.blogspot.com/2016/08/consuming-restful-data.html).

Tech blog: http://jubitoblog.blogspot.com\
FAQ: http://jubito.org/faq.html

## Jubito Screenshot
![screenshot](https://1.bp.blogspot.com/-zckBAkF6q9k/V_nE97h0_BI/AAAAAAAAJDU/6fXFVP5eSOEj9cTG5XMDgVVLL10ySnLWQCLcB/s640/dashboard-main.png)
