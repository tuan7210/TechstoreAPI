CREATE DATABASE tech_store;
USE tech_store;
CREATE TABLE User (
    user_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    email VARCHAR(255) UNIQUE,
    password_hash VARCHAR(255),
    role ENUM('customer','admin'),
    phone VARCHAR(20),
    address TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

CREATE TABLE Category (
    category_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    description TEXT
);

CREATE TABLE Product (
    product_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    description TEXT,
    price DECIMAL(10,2),
    stock_quantity INT,
    category_id INT,
    image_url VARCHAR(255),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (category_id) REFERENCES Category(category_id)
);

CREATE TABLE Cart (
    cart_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    FOREIGN KEY (user_id) REFERENCES User(user_id)
);

CREATE TABLE CartItem (
    cart_item_id INT AUTO_INCREMENT PRIMARY KEY,
    cart_id INT,
    product_id INT,
    quantity INT,
    FOREIGN KEY (cart_id) REFERENCES Cart(cart_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id)
);

CREATE TABLE OrderTable (
    order_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status ENUM('pending', 'shipped', 'completed', 'canceled'),
    total_amount DECIMAL(10,2),
    shipping_address TEXT,
    payment_status ENUM('unpaid', 'paid'),
    payment_method VARCHAR(50),
    FOREIGN KEY (user_id) REFERENCES User(user_id)
);

CREATE TABLE OrderItem (
    order_item_id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT,
    product_id INT,
    quantity INT,
    price DECIMAL(10,2),
    FOREIGN KEY (order_id) REFERENCES OrderTable(order_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id)
);

CREATE TABLE Review (
    review_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    product_id INT,
    order_item_id INT,
    rating INT CHECK (rating BETWEEN 1 AND 5),
    comment TEXT,
    is_verified BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES User(user_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id),
    FOREIGN KEY (order_item_id) REFERENCES OrderItem(order_item_id),
    UNIQUE KEY unique_review_per_order_item (order_item_id),
    INDEX idx_product_rating (product_id, rating),
    INDEX idx_user_reviews (user_id, created_at)
);

-- Trigger để tự động verify review khi order đã completed
DELIMITER //
CREATE TRIGGER verify_review_after_insert
    AFTER INSERT ON Review
    FOR EACH ROW
BEGIN
    DECLARE order_status VARCHAR(20);
    
    SELECT ot.status INTO order_status
    FROM OrderTable ot
    JOIN OrderItem oi ON ot.order_id = oi.order_id
    WHERE oi.order_item_id = NEW.order_item_id;
    
    IF order_status = 'completed' THEN
        UPDATE Review 
        SET is_verified = TRUE 
        WHERE review_id = NEW.review_id;
    END IF;
END//
DELIMITER ;

-- Trigger để verify review khi order status thay đổi thành completed
DELIMITER //
CREATE TRIGGER verify_reviews_on_order_complete
    AFTER UPDATE ON OrderTable
    FOR EACH ROW
BEGIN
    IF NEW.status = 'completed' AND OLD.status != 'completed' THEN
        UPDATE Review r
        JOIN OrderItem oi ON r.order_item_id = oi.order_item_id
        SET r.is_verified = TRUE
        WHERE oi.order_id = NEW.order_id;
    END IF;
END//
DELIMITER ;
INSERT INTO Category (name, description) VALUES
('Điện thoại', 'Các loại điện thoại thông minh, điện thoại phổ thông'),
('Laptop', 'Các loại máy tính xách tay, laptop gaming, laptop văn phòng'),
('Phụ kiện', 'Tai nghe, sạc, cáp, ốp lưng, chuột máy tính');

INSERT INTO Product (name, description, price, stock_quantity, category_id, image_url) VALUES
-- Điện thoại
('iPhone 15 128GB', 'iPhone 15 chính hãng VN/A, màu đen', 25000000, 50, 1, 'https://example.com/images/iphone15.jpg'),
('Samsung Galaxy S24', 'Samsung Galaxy S24 chính hãng, màu trắng', 22000000, 40, 1, 'https://example.com/images/galaxy_s24.jpg'),
('Xiaomi Redmi Note 13', 'Redmi Note 13 5G chính hãng, màu xanh', 7000000, 60, 1, 'https://example.com/images/redmi_note13.jpg'),

-- Laptop
('MacBook Air M3 2025', 'MacBook Air M3 8GB/256GB, màu bạc', 32000000, 30, 2, 'https://example.com/images/macbook_air_m3.jpg'),
('Dell XPS 13', 'Dell XPS 13 2025, i7, 16GB RAM, 512GB SSD', 35000000, 25, 2, 'https://example.com/images/dell_xps_13.jpg'),
('ASUS ROG Zephyrus G14', 'Laptop gaming ASUS ROG G14, Ryzen 9, 32GB RAM', 42000000, 20, 2, 'https://example.com/images/asus_rog_g14.jpg'),

-- Phụ kiện
('Tai nghe AirPods Pro 2', 'Tai nghe AirPods Pro 2 chính hãng Apple', 5200000, 80, 3, 'https://example.com/images/airpods_pro_2.jpg'),
('Sạc nhanh Anker 65W', 'Sạc nhanh Anker 65W PowerPort, sạc laptop & điện thoại', 850000, 100, 3, 'https://example.com/images/anker_65w.jpg'),
('Chuột Logitech MX Master 3', 'Chuột không dây Logitech MX Master 3', 2500000, 50, 3, 'https://example.com/images/logitech_mx_master_3.jpg');

