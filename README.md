# Custom Packaging Logic for Acumatica Shipments  

## Overview  

This project provides an enhanced logic for creating packages on the Shipment screen in Acumatica, specifically addressing scenarios where **Master Boxes** are required.  

By default, Acumatica does not support packing smaller boxes into larger ones (Master Boxes). This customization enables precise package distribution based on **Weight, Volume, or a combination of both**.  

## Features  

- **Overrides the default CreateShipment method** to allow custom package allocation.  
- **Removes default "Auto" packages** created by Acumatica.  
- **Implements custom logic for package distribution** based on Weight, Volume, or both.  
- **Dynamically selects the best-suited package size** to optimize shipments.  

## Implementation  

1. **Overriding CreateShipment**  
   - The `CreateShipment` method is overridden using `[PXOverride]`.  
   - Before executing the default logic, all necessary package modifications are applied.  

2. **Deleting Default Auto Packages**  
   - Acumatica assigns packages automatically; these need to be removed for manual control.  

3. **Applying Custom Packaging Logic**  
   - The logic loops through shipment lines and assigns items to packages.  
   - A method like `CreatePackagesByWeight` ensures items are packed optimally.  

4. **Handling Weight and Volume Constraints**  
   - The system calculates the best packaging approach considering weight and volume.  
   - If items exceed the current package capacity, a new suitable box is selected.  

## Read More  

For a detailed explanation, visit our blog:  
[Overriding Logic for Creating Packages for Shipment Screen](https://blog.zaletskyy.com/post/2025/01/30/overriding-logic-for-creating-packages-for-shipment-screen-by-weight_volume-or-weight-and-volume)  

## Need Customization?  

If you're facing similar challenges with Acumatica’s default logic or need tailored solutions, we can help!  
Leave us a request here:  
[Contact Us](https://acupowererp.com/contact-us)  
